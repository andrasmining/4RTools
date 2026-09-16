from pathlib import Path
import re


def replace_one(path, old, new):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one replacement, found {count}: {old[:100]!r}')
    p.write_text(text.replace(old, new), encoding='utf-8', newline='')


def insert_before(path, marker, addition):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    if addition.strip() in text:
        return
    count = text.count(marker)
    if count != 1:
        raise SystemExit(f'{path}: expected one insertion marker, found {count}')
    p.write_text(text.replace(marker, addition + marker), encoding='utf-8', newline='')

startup = 'Model/Vanilla/VanillaStartupOrchestrator.cs'
old_start = '''                    WaitForCharacterSurface(input, pid.Value, generation);\n                    int slot = account.RequiredCharacterSlot() - 1;\n                    int col = slot % 5, row = slot / 5;\n                    input.Activate();\n                    BriefPause(generation, 120);\n                    input.ClickNormalized(config.Anchors.CharacterGridX + col * config.Anchors.CharacterStepX,\n                        config.Anchors.CharacterGridY + row * config.Anchors.CharacterStepY);\n                    BriefPause(generation, 220);\n                    input.Activate();\n                    input.ClickNormalized(config.Anchors.GameStartX, config.Anchors.GameStartY);\n                    Log(account.Label + ": character slot " + account.CharacterSlot + " selected; GAME START clicked with foreground verified.");\n                    VanillaDebugLog.Write("STARTUP", account.Label + ": character slot selected and GAME START clicked.");\n'''
new_start = '''                    WaitForCharacterSurface(input, pid.Value, generation);\n                    SelectConfiguredCharacterWithoutCoordinates(input, pid.Value, account,\n                        () => StartupCancelled(generation) || ResumeWorkerCancelled(runtime, pid.Value, resumeGeneration),\n                        account.Label + ": sequential: ");\n'''
replace_one(startup, old_start, new_start)

helpers = r'''        internal static Keys[] CharacterSelectionKeyPlan(int oneBasedSlot)
        {
            if (oneBasedSlot < 1 || oneBasedSlot > 15)
                throw new ArgumentOutOfRangeException(nameof(oneBasedSlot));
            int zero = oneBasedSlot - 1;
            int row = zero / 5, column = zero % 5;
            var keys = new System.Collections.Generic.List<Keys>();
            // Character selection owns keyboard focus. Clamp to the top-left card first,
            // then navigate from a known origin. This is independent of resolution/DPI.
            keys.Add(Keys.Up); keys.Add(Keys.Up);
            for (int i = 0; i < 4; i++) keys.Add(Keys.Left);
            for (int i = 0; i < column; i++) keys.Add(Keys.Right);
            for (int i = 0; i < row; i++) keys.Add(Keys.Down);
            keys.Add(Keys.Enter);
            return keys.ToArray();
        }

        private void SelectConfiguredCharacterWithoutCoordinates(VanillaForegroundInput input, int pid,
            VanillaReconnectAccount account, Func<bool> cancelled, string logPrefix)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (cancelled == null) throw new ArgumentNullException(nameof(cancelled));
            int slot = account.RequiredCharacterSlot();
            Keys[] plan = CharacterSelectionKeyPlan(slot);
            input.Activate();
            for (int i = 0; i < plan.Length; i++)
            {
                if (cancelled()) throw new OperationCanceledException("Character selection cancelled before input.");
                input.Press(plan[i]);
                if (i + 1 < plan.Length) PauseCharacterSelection(cancelled, 70);
            }
            string expected = string.IsNullOrWhiteSpace(account.CharacterName) ? "<learn after gameplay>" : account.CharacterName;
            string detail = logPrefix + "character selection used keyboard-only navigation to configured slot " + slot
                + " for '" + expected + "'; no character-grid or GAME START coordinates were clicked.";
            Log(detail);
            VanillaDebugLog.Write("STARTUP", "PID=" + pid + "; " + detail);
        }

        private void WaitForCharacterSurfaceCancellable(VanillaForegroundInput input, int pid, Func<bool> cancelled,
            int timeoutMs, string context)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int consecutive = 0;
            string last = "not sampled";
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (cancelled()) throw new OperationCanceledException(context + ": character selection cancelled.");
                using (Bitmap image = input.CaptureClientBitmap())
                {
                    VanillaLoginLayout login;
                    VanillaServerLayout server;
                    VanillaProxyLayout proxyLayout;
                    string evidence;
                    bool loginVisible = VanillaAuthPattern.TryDetectLogin(image, out login, out evidence);
                    bool serverVisible = VanillaAuthPattern.TryDetectServerDialog(image, out server, out evidence);
                    bool proxyVisible = VanillaProxyPattern.TryDetect(image, out proxyLayout, out evidence);
                    bool interactive = IsInteractiveFrame(image);
                    last = "login=" + loginVisible + ", server=" + serverVisible + ", proxy=" + proxyVisible + ", interactive=" + interactive;
                    if (!loginVisible && !serverVisible && !proxyVisible && interactive && watch.ElapsedMilliseconds >= 450)
                    {
                        consecutive++;
                        if (consecutive >= 3)
                        {
                            SaveUiCapture(image, "character-screen-ready.png");
                            VanillaDebugLog.Write("STARTUP", context + ": character surface PID=" + pid
                                + " stable after " + watch.ElapsedMilliseconds + " ms; " + last + ".");
                            return;
                        }
                    }
                    else consecutive = 0;
                }
                PauseCharacterSelection(cancelled, 160);
            }
            throw new InvalidOperationException(context + ": character surface was not safely detected within "
                + (timeoutMs / 1000) + "s. No character-selection input was sent. Last state: " + last);
        }

        private static void PauseCharacterSelection(Func<bool> cancelled, int milliseconds)
        {
            int remaining = Math.Max(0, milliseconds);
            while (remaining > 0)
            {
                if (cancelled()) throw new OperationCanceledException("Character selection cancelled.");
                int slice = Math.Min(50, remaining);
                Thread.Sleep(slice);
                remaining -= slice;
            }
        }

'''
insert_before(startup, '        private void SelectProxyWhenVisible', helpers)

reconnect = 'Model/Vanilla/VanillaReconnect.cs'
old_login = '''                    int slot = account.RequiredCharacterSlot() - 1;\n                    int col = slot % 5, row = slot / 5;\n                    input.ClickNormalized(config.Anchors.CharacterGridX + col * config.Anchors.CharacterStepX,\n                        config.Anchors.CharacterGridY + row * config.Anchors.CharacterStepY);\n                    Thread.Sleep(350);\n                    input.ClickNormalized(config.Anchors.GameStartX, config.Anchors.GameStartY);\n                    Thread.Sleep(config.GameLoadMs);\n'''
new_login = '''                    WaitForCharacterSurfaceCancellable(input, pid, cancelled, 30000, account.Label + ": recovery");\n                    SelectConfiguredCharacterWithoutCoordinates(input, pid, account, cancelled, account.Label + ": recovery: ");\n                    PauseCharacterSelection(cancelled, config.GameLoadMs);\n'''
replace_one(reconnect, old_login, new_login)

# Regression test every target slot from every possible initial slot using the exact key plan.
tests = 'Tests/VanillaReconnectRegressionTests.cs'
replace_one(tests,
'''            Test("Saved account catalog keeps extra profiles but limits enabled clients", SavedAccountCatalog);\n''',
'''            Test("Saved account catalog keeps extra profiles but limits enabled clients", SavedAccountCatalog);\n            Test("Character selection reaches every configured slot without coordinates", CharacterSelectionKeyboardPlan);\n''')
insert_before(tests, '        private static void FreshDefaults()', r'''        private static void CharacterSelectionKeyboardPlan()
        {
            for (int target = 1; target <= 15; target++)
            {
                var plan = VanillaReconnectSupervisor.CharacterSelectionKeyPlan(target);
                Assert(plan.Length >= 7 && plan.Last() == System.Windows.Forms.Keys.Enter,
                    "Selection plan must finish with Enter and contain its clamp sequence.");
                for (int start = 1; start <= 15; start++)
                {
                    int position = start - 1;
                    int row = position / 5, col = position % 5;
                    foreach (var key in plan.Take(plan.Length - 1))
                    {
                        if (key == System.Windows.Forms.Keys.Up) row = Math.Max(0, row - 1);
                        else if (key == System.Windows.Forms.Keys.Down) row = Math.Min(2, row + 1);
                        else if (key == System.Windows.Forms.Keys.Left) col = Math.Max(0, col - 1);
                        else if (key == System.Windows.Forms.Keys.Right) col = Math.Min(4, col + 1);
                        else throw new Exception("Unexpected character-selection key: " + key);
                    }
                    int reached = row * 5 + col + 1;
                    Assert(reached == target, "Start slot " + start + " reached " + reached + " instead of " + target + ".");
                }
            }
        }

''')

# Durable policy: character selection must not return to fixed coordinate anchors.
agents = 'AGENTS.md'
insert_before(agents, '## Cleanliness, documentation, and final reporting', r'''## Resolution-agnostic character selection

Never select a Vanilla character or GAME START by fixed/normalized grid coordinates.
Character-select automation must use focus-verified keyboard navigation from a
clamped known origin, driven by the configured one-based slot, and the existing
read-only username + character-name identity must verify the resulting gameplay
before any Autobattle hotkey is sent. Character-name evidence is preferred when
available, but unavailable selection-screen memory must not be guessed or replaced
with OCR/coordinate assumptions. Unknown or contradictory identity fails closed.
Keep this path resolution/DPI agnostic and cover every target slot from every
possible initial selection in deterministic tests. Any future visual interaction
must detect the actual control/region first and click inside that detected region;
do not reintroduce hard-coded character-grid or GAME START coordinates.

''')

# Bump patch release.
assembly = 'Properties/AssemblyInfo.cs'
p = Path(assembly)
text = p.read_text(encoding='utf-8')
text2 = text.replace('AssemblyVersion("0.6.38.0")', 'AssemblyVersion("0.6.39.0")').replace(
    'AssemblyFileVersion("0.6.38.0")', 'AssemblyFileVersion("0.6.39.0")')
if text2 == text:
    raise SystemExit('Assembly version 0.6.38.0 not found')
p.write_text(text2, encoding='utf-8', newline='')

notes = Path('RELEASE-NOTES.md')
text = notes.read_text(encoding='utf-8')
if not text.startswith('# 4RTools Vanilla 0.6.38'):
    raise SystemExit('Unexpected release-notes version')
intro = '''# 4RTools Vanilla 0.6.39\n\n## Resolution-agnostic character selection\n\nCharacter selection no longer clicks configured character-grid or GAME START\ncoordinates. After the character screen is stably detected and foreground ownership\nis verified, the client is driven only with keyboard navigation: Up/Left clamp the\nselection to the top-left card, Right/Down move to the configured one-based slot,\nand Enter starts that character. The same path is used for cold startup and recovery.\nThis removes the resolution/DPI failure that could click an empty card and open the\nnew-character flow.\n\nEvery one of the 15 target slots is regression-tested from every possible initial\nselection. Existing read-only username + character-name checks still verify the\nactual gameplay identity before the Autobattle resume hotkey; a mismatch fails closed.\nThe supplied incident screenshot showed slot 2 required while a coordinate click landed\non an empty card, which is the regression this release removes.\n\n'''
text = intro + text.split('\n', 1)[1]
notes.write_text(text, encoding='utf-8', newline='')

# Remove stale wording that advertises coordinate character clicking if present.
for path in ['README.md', 'packaging/README.txt']:
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    marker = 'Character selection is keyboard-driven from a clamped grid origin; fixed character-slot and GAME START coordinates are not used.\n'
    if marker not in text:
        text += '\n' + marker
    p.write_text(text, encoding='utf-8', newline='')

print('Character-selection patch applied.')