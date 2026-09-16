using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    internal sealed partial class VanillaReconnectForm
    {
        private Timer characterDiscoveryTimer;
        private bool characterEditorOpen;
        private readonly HashSet<string> ignoredDiscoveredCharacters = new HashSet<string>(StringComparer.Ordinal);
        private string lastDiscoveryError;

        internal bool CharacterDiscoveryActive { get { return characterDiscoveryTimer?.Enabled == true; } }

        private void InstallCharacterDiscovery()
        {
            if (!observeCharacterDiscovery || characterDiscoveryTimer != null || accountCatalogStore == null) return;
            characterDiscoveryTimer = new Timer { Interval = 1000 };
            characterDiscoveryTimer.Tick += (s, e) => DiscoverCharacters(false);
            DiscoverCharacters(false);
            characterDiscoveryTimer.Start();
        }

        private void DiscoverCharacters(bool explicitRequest)
        {
            if (IsDisposed || characterEditorOpen || accountCatalog == null || accountCatalogStore == null) return;
            if (explicitRequest) ignoredDiscoveredCharacters.Clear();
            try
            {
                var candidate = accountCatalog.Select(r => r.Clone()).ToList();
                bool changed = false;
                foreach (var confirmed in supervisor.ConfirmedCharacters())
                {
                    var row = candidate.FirstOrDefault(r => r.Id == confirmed.Key);
                    if (row != null && !candidate.Any(r => r.Id != row.Id && VanillaCharacterRoster.Same(r.CharacterName, confirmed.Value.CharacterName)))
                        changed |= VanillaCharacterRoster.FillMissing(row, confirmed.Value);
                }
                changed |= VanillaCharacterRoster.MergeObserved(candidate, supervisor.ObservedCharacters()
                    .Where(i => i != null && !supervisor.CharacterDiscoveryPending(i.ProcessId)), DateTimeOffset.UtcNow, ignoredDiscoveredCharacters);
                if (changed)
                {
                    // Persist before replacing UI state. No discovery failure can discard saved credentials.
                    accountCatalogStore.Save(candidate);
                    accountCatalog = candidate;
                    supervisor.EnrichCharacterMetadata(accountCatalog);
                    SynchronizeSupervisorAccountsFromCatalog();
                    if (!supervisor.IsRunning && !supervisor.IsHardenedStartupRunning)
                        supervisor.Apply(settings, true);
                    RefreshAccountGridFromCatalog();
                    ScheduleResponsiveRecoveryLayout();
                    ShowSaveToast("Character list updated", false);
                    VanillaDebugLog.Write("IDENTITY", "Character catalog updated from verified read-only observations; rows=" + candidate.Count + ". New rows stay disabled.");
                }
                lastDiscoveryError = null;
                RefreshAccountSupplementalColumns();
            }
            catch (Exception ex)
            {
                if (lastDiscoveryError != ex.Message)
                {
                    VanillaDebugLog.Write("IDENTITY", "Character discovery/save failed: " + ex.Message);
                    ShowSaveToast("Character discovery/save failed", true);
                    lastDiscoveryError = ex.Message;
                }
            }
        }

        private void StopCharacterDiscovery()
        {
            characterDiscoveryTimer?.Stop();
            characterDiscoveryTimer?.Dispose();
            characterDiscoveryTimer = null;
        }
    }
}
