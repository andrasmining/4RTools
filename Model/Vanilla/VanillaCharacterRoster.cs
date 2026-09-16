using System;
using System.Collections.Generic;
using System.Linq;

namespace _4RTools.Model.Vanilla
{
    /// <summary>Only independently verified fields from one read-only client snapshot.</summary>
    internal sealed class VanillaCharacterIdentity
    {
        internal readonly int ProcessId;
        internal readonly Guid Session;
        internal readonly DateTimeOffset At;
        internal readonly string CharacterName, UserName;
        internal readonly int? CharacterSlot;

        internal VanillaCharacterIdentity(int processId, Guid session, DateTimeOffset at,
            string characterName, string userName = null, int? characterSlot = null)
        {
            ProcessId = processId; Session = session; At = at;
            CharacterName = KnownText(characterName, 80);
            UserName = KnownText(userName, 128);
            CharacterSlot = characterSlot >= 1 && characterSlot <= 15 ? characterSlot : null;
        }

        internal bool IsFresh(DateTimeOffset now)
        {
            return ProcessId > 0 && Session != Guid.Empty && CharacterName != null
                && now >= At && now - At <= TimeSpan.FromSeconds(3);
        }

        internal static string KnownText(string value, int max)
        {
            return string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)
                ? null : value.Trim();
        }

        internal static VanillaCharacterIdentity FromState(VanillaClientState state)
        {
            if (state == null || state.IsDemo || state.Error != null || !state.ProcessId.HasValue) return null;
            Func<VanillaField, bool> valid = field => state.Fields != null && state.Fields.ContainsKey(field)
                && state.Fields[field] != null && state.Fields[field].IsAvailable && state.Fields[field].Validation == StateValidation.Valid
                && state.Fields[field].LastObservedAtUtc == state.SampledAtUtc;
            if ((valid(VanillaField.Loading) && state.Loading.Value)
                || (valid(VanillaField.ClientReady) && !state.ClientReady.Value)) return null;
            return new VanillaCharacterIdentity(state.ProcessId.Value, state.SessionId, state.SampledAtUtc,
                valid(VanillaField.CharacterName) ? state.CharacterName.Value : null,
                valid(VanillaField.UserName) ? state.UserName.Value : null,
                valid(VanillaField.CharacterSlot) ? (int?)state.CharacterSlot.Value : null);
        }
    }

    internal static class VanillaCharacterRoster
    {
        internal static bool Same(string left, string right)
        { return string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.Ordinal); }

        internal static bool Matches(VanillaReconnectAccount row, VanillaCharacterIdentity identity, DateTimeOffset now)
        {
            if (row == null || identity == null || !identity.IsFresh(now)) return false;
            bool named = !string.IsNullOrWhiteSpace(row.CharacterName);
            if (named && !Same(row.CharacterName, identity.CharacterName)) return false;
            if (!string.IsNullOrWhiteSpace(row.UserName) && identity.UserName != null && !Same(row.UserName, identity.UserName)) return false;
            if (row.CharacterSlot.HasValue && identity.CharacterSlot.HasValue && row.CharacterSlot != identity.CharacterSlot) return false;
            // A username alone is never a character identity. Legacy rows need both username and slot.
            return named || (!string.IsNullOrWhiteSpace(row.UserName) && identity.UserName != null
                && row.CharacterSlot.HasValue && identity.CharacterSlot.HasValue);
        }

        internal static VanillaCharacterIdentity FindUnique(VanillaReconnectAccount row,
            IEnumerable<VanillaCharacterIdentity> observed, IEnumerable<int> eligiblePids, DateTimeOffset now)
        {
            var eligible = new HashSet<int>(eligiblePids ?? Enumerable.Empty<int>());
            var matches = (observed ?? Enumerable.Empty<VanillaCharacterIdentity>())
                .Where(i => i != null && eligible.Contains(i.ProcessId) && Matches(row, i, now)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        internal static bool FillMissing(VanillaReconnectAccount row, VanillaCharacterIdentity identity)
        {
            bool changed = false;
            if (string.IsNullOrWhiteSpace(row.CharacterName) && identity.CharacterName != null)
            { row.CharacterName = identity.CharacterName; changed = true; }
            if (string.IsNullOrWhiteSpace(row.UserName) && identity.UserName != null)
            { row.UserName = identity.UserName; changed = true; }
            if (!row.CharacterSlot.HasValue && identity.CharacterSlot.HasValue)
            { row.CharacterSlot = identity.CharacterSlot; changed = true; }
            return changed;
        }

        internal static bool MergeObserved(IList<VanillaReconnectAccount> rows,
            IEnumerable<VanillaCharacterIdentity> observed, DateTimeOffset now, ISet<string> ignored = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            bool changed = false;
            var unique = (observed ?? Enumerable.Empty<VanillaCharacterIdentity>())
                .Where(i => i != null && i.IsFresh(now))
                .GroupBy(i => i.CharacterName, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Single());
            foreach (var identity in unique)
            {
                if (ignored != null && ignored.Contains(identity.CharacterName)) continue;
                var named = rows.Where(r => Same(r.CharacterName, identity.CharacterName)).ToArray();
                if (named.Length > 0)
                {
                    if (named.Length == 1 && Matches(named[0], identity, now)) changed |= FillMissing(named[0], identity);
                    continue; // Duplicate or contradictory identity must never overwrite credentials or create another row.
                }
                var legacy = rows.Where(r => string.IsNullOrWhiteSpace(r.CharacterName) && Matches(r, identity, now)).ToArray();
                if (legacy.Length == 1) { changed |= FillMissing(legacy[0], identity); continue; }
                if (legacy.Length > 1) continue;
                rows.Add(new VanillaReconnectAccount
                {
                    Label = identity.CharacterName, CharacterName = identity.CharacterName,
                    UserName = identity.UserName ?? string.Empty, CharacterSlot = identity.CharacterSlot,
                    Enabled = false, ProxyNeedsConfiguration = true
                });
                changed = true;
            }
            if (changed && rows.Any(r => !string.IsNullOrWhiteSpace(r.CharacterName)))
            {
                // Remove only untouched synthetic defaults, never a user-configured row.
                foreach (var empty in rows.Where(IsEmptyDefault).ToArray()) rows.Remove(empty);
            }
            return changed;
        }

        internal static bool IsEmptyDefault(VanillaReconnectAccount row)
        {
            return row != null && string.IsNullOrWhiteSpace(row.CharacterName) && string.IsNullOrWhiteSpace(row.UserName)
                && string.IsNullOrWhiteSpace(row.ProtectedPassword)
                && (row.Label == "Client" || row.Label == "Client 1" || row.Label == "Client 2");
        }

        internal static void Validate(IList<VanillaReconnectAccount> rows)
        {
            if (rows == null || rows.Count == 0) throw new InvalidOperationException("Keep at least one character profile.");
            if (rows.Any(r => r == null || string.IsNullOrWhiteSpace(r.Id))
                || rows.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Character profile IDs must be unique.");
            if (rows.Count(r => r.Enabled) > 2) throw new InvalidOperationException("Only two characters may be enabled at once.");
            if (rows.Where(r => !string.IsNullOrWhiteSpace(r.CharacterName))
                .GroupBy(r => r.CharacterName.Trim(), StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Each character must have only one row. Edit the existing character instead.");
            foreach (var row in rows)
            {
                if (KnownInvalid(row.Label, 80) || string.IsNullOrWhiteSpace(row.Label)
                    || KnownInvalid(row.CharacterName, 80) || KnownInvalid(row.UserName, 128))
                    throw new InvalidOperationException("Description, username or character name is invalid.");
                if (row.CharacterSlot.HasValue && (row.CharacterSlot < 1 || row.CharacterSlot > 15))
                    throw new InvalidOperationException("Character slot must be 1 to 15, or unknown.");
            }
        }

        private static bool KnownInvalid(string value, int max)
        { return value != null && (value.Length > max || value.Any(char.IsControl)); }
    }

    public sealed partial class VanillaFleetMonitor
    {
        private IReadOnlyList<VanillaCharacterIdentity> characterCache = new VanillaCharacterIdentity[0];
        internal IReadOnlyList<VanillaCharacterIdentity> LatestCharacters()
        { return System.Threading.Volatile.Read(ref characterCache); }
        private void PublishCharacters(IReadOnlyList<VanillaFleetClientInfo> clients)
        {
            System.Threading.Volatile.Write(ref characterCache,
                Array.AsReadOnly(clients.Where(c => c.Identity != null && c.Error == null).Select(c => c.Identity).ToArray()));
        }
    }
}
