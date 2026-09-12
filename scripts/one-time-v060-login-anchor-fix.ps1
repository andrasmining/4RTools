Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p) { [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }

$p='Model/Vanilla/VanillaReconnect.cs'
$t=ReadText $p
$old='        public double UserNameY { get; set; } = 0.635;'
if(-not $t.Contains($old)){throw 'Current username anchor missing'}
$t=$t.Replace($old,'        public double UserNameY { get; set; } = 0.677;')
$old='        public double PasswordY { get; set; } = 0.660;'
if(-not $t.Contains($old)){throw 'Current password anchor missing'}
$t=$t.Replace($old,'        public double PasswordY { get; set; } = 0.697;')
$old='            if (Math.Abs(Anchors.UserNameY - 0.66) < 0.0001 && Math.Abs(Anchors.PasswordY - 0.685) < 0.0001) { Anchors.UserNameY = 0.635; Anchors.PasswordY = 0.660; }'
if(-not $t.Contains($old)){throw 'Anchor migration statement missing'}
$new='            bool legacyLoginAnchors = (Math.Abs(Anchors.UserNameY - 0.66) < 0.0001 && Math.Abs(Anchors.PasswordY - 0.685) < 0.0001)'+[Environment]::NewLine+
'                || (Math.Abs(Anchors.UserNameY - 0.635) < 0.0001 && Math.Abs(Anchors.PasswordY - 0.660) < 0.0001);'+[Environment]::NewLine+
'            if (legacyLoginAnchors) { Anchors.UserNameY = 0.677; Anchors.PasswordY = 0.697; }'
$t=$t.Replace($old,$new)
WriteText $p $t

$p='Tests/VanillaReconnectRegressionTests.cs'
$t=ReadText $p
$old='            Test("Vanilla Launcher.exe uses GAME START launcher mode", VanillaLauncherName);'
if(-not $t.Contains($old)){throw 'Reconnect test list anchor missing'}
$t=$t.Replace($old,$old+[Environment]::NewLine+'            Test("Legacy login anchors migrate to verified field centers", LoginAnchorMigration);')
$anchor='        private static string Temp()'
if(-not $t.Contains($anchor)){throw 'Reconnect test method anchor missing'}
$method=@'
        private static void LoginAnchorMigration()
        {
            var legacy = VanillaReconnectSettings.CreateDefault();
            legacy.Anchors.UserNameY = 0.66;
            legacy.Anchors.PasswordY = 0.685;
            var migrated = legacy.Clone();
            Assert(Math.Abs(migrated.Anchors.UserNameY - 0.677) < 0.0001 && Math.Abs(migrated.Anchors.PasswordY - 0.697) < 0.0001,
                "Legacy login-field coordinates were not migrated.");

            var interim = VanillaReconnectSettings.CreateDefault();
            interim.Anchors.UserNameY = 0.635;
            interim.Anchors.PasswordY = 0.660;
            migrated = interim.Clone();
            Assert(Math.Abs(migrated.Anchors.UserNameY - 0.677) < 0.0001 && Math.Abs(migrated.Anchors.PasswordY - 0.697) < 0.0001,
                "Interim login-field coordinates were not migrated.");
        }
'@
$t=$t.Replace($anchor,$method+[Environment]::NewLine+$anchor)
WriteText $p $t
