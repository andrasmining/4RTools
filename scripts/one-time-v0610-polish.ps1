Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
function ReadText([string]$path) { [IO.File]::ReadAllText($path) }
function WriteText([string]$path,[string]$text) { [IO.File]::WriteAllText($path,$text,$utf8) }

$path = 'Model/Vanilla/VanillaIntegratedShell.cs'
$text = ReadText $path
$oldUsing = "using _4RTools.Model;`r`nusing _4RTools.Model.Vanilla.Automation;"
$newUsing = "using _4RTools.Model;`r`nusing _4RTools.Model.Vanilla;`r`nusing _4RTools.Model.Vanilla.Automation;"
if (-not $text.Contains($oldUsing)) {
    $oldUsing = "using _4RTools.Model;`nusing _4RTools.Model.Vanilla.Automation;"
    $newUsing = "using _4RTools.Model;`nusing _4RTools.Model.Vanilla;`nusing _4RTools.Model.Vanilla.Automation;"
}
if (-not $text.Contains($oldUsing)) { throw 'VanillaIntegratedShell using anchor not found.' }
$text = $text.Replace($oldUsing,$newUsing)
WriteText $path $text

$path = 'Model/Vanilla/VanillaTemporaryActions.cs'
$text = ReadText $path
$old = @'
            box.DataSource = Enum.GetValues(typeof(Keys)).Cast<Keys>().Select(key => (int)key).Where(value => value >= 8 && value <= 254)
                .Distinct().OrderBy(value => value).Select(value => new KeyChoice(value, ((Keys)value).ToString())).ToArray();
'@
$new = @'
            var choices = new[] { new KeyChoice(0, "None") }.Concat(
                Enum.GetValues(typeof(Keys)).Cast<Keys>().Select(key => (int)key).Where(value => value >= 8 && value <= 254)
                    .Distinct().OrderBy(value => value).Select(value => new KeyChoice(value, ((Keys)value).ToString()))).ToArray();
            box.DataSource = choices;
'@
if (-not $text.Contains($old)) { throw 'Temporary action key selector anchor not found.' }
$text = $text.Replace($old,$new)
WriteText $path $text

$agents = 'AGENTS.md'
$text = ReadText $agents
$old = @'
The original 4RTools window is the primary application. Add Vanilla to its
Ragnarok Client selector through the read-only adapter, reuse existing feature
forms and verified state readers, and extend that application where needed.
Diagnostics and discovery support address mapping; they are not a replacement
product. Additional Vanilla controls may open as an owned window from the
original interface. Preserve useful existing extensions while integrating them.
Selecting a process does not prove its gameplay addresses or feature support.
'@
$new = @'
The Vanilla workspace is now the primary product surface. Keep it first and use
it for recovery, live read-only client state, automation, temporary actions,
diagnostics, updates, and future Vanilla-specific features. Up to two Vanilla
clients should be visible and manageable without switching to the legacy UI.
The original 4RTools interface remains available only as a secondary compatibility
tab; preserve useful stock code and reuse proven memory/input components, but do
not make new Vanilla workflows depend on the legacy single-client selector.
Diagnostics and discovery support address mapping and verification. Selecting a
process or reading bytes does not by itself prove gameplay semantics.
'@
if (-not $text.Contains($old)) { throw 'AGENTS product direction anchor not found.' }
$text = $text.Replace($old,$new)
WriteText $agents $text

Write-Host '0.6.10 workspace compile fix and polish applied.'
