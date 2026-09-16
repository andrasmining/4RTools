from pathlib import Path


def rewrite(path, edits):
    p = Path(path)
    raw = p.read_bytes()
    crlf = b'\r\n' in raw
    text = raw.decode('utf-8').replace('\r\n', '\n')
    for old, new, count in edits:
        actual = text.count(old)
        assert actual == count, (path, old[:120], actual, count)
        text = text.replace(old, new, count)
    if crlf:
        text = text.replace('\n', '\r\n')
    p.write_bytes(text.encode('utf-8'))


rewrite('Model/Vanilla/VanillaCharacterBinding.cs', [
    ('            runtime.AutobattleRestartAttempts = 0;\n', '', 1),
    ('            runtime.AutobattleRestartInProgress = false;\n', '', 1),
    ('            runtime.AutobattleRecoveryExhausted = false;\n', '', 1),
])

rewrite('Tests/VanillaRecoveryWatchdogTests.cs', [
    ('''            internal bool Motion(object runtime)\n            { return (bool)Call(Supervisor, "CheckMovementWatchdog", runtime, E.UtcNow, (Func<DateTime>)(() => Epoch.UtcDateTime), (Func<VanillaVisualState>)(() => VanillaVisualState.Unknown)); }\n''',
     '''            internal bool Motion(object runtime)\n            { return (bool)Call(Supervisor, "CheckMovementWatchdog", runtime, E.UtcNow, (Func<DateTime>)(() => Epoch.UtcDateTime)); }\n''', 1),
])

print('Removed obsolete finite restart state and aligned the recovery harness.')
