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


def rewrite_method(path, name, edit):
    p = Path(path)
    raw = p.read_bytes()
    crlf = b'\r\n' in raw
    text = raw.decode('utf-8').replace('\r\n', '\n')
    marker = '        private static void ' + name + '()\n'
    i = text.find(marker)
    assert i >= 0, (path, name)
    j = text.find('        private static void ', i + len(marker))
    assert j >= 0, (path, name, 'next method')
    block = text[i:j]
    changed = edit(block)
    assert changed != block, (path, name, 'no change')
    text = text[:i] + changed + text[j:]
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

# The main transformer deliberately arms a five-minute X/Y stall before terminal
# popup tests. Keep all subsequent fake-clock timestamps monotonic; the old tests
# jumped back to 1/2/3 seconds because terminal recovery used to be immediate.
for method in ('Stop', 'StopAtBoundary', 'Settings', 'ReplacedPid', 'ReplacedOperation'):
    def bump(block, method=method):
        assert 'ArmFiveMinuteStall' in block, (method, 'stall not armed')
        assert block.count('h.E.Seconds=1') + block.count('h.E.Seconds = 1') == 1, (method, 'first confirmation timestamp')
        return block.replace('h.E.Seconds=1', 'h.E.Seconds=301', 1).replace('h.E.Seconds = 1', 'h.E.Seconds = 301', 1)
    rewrite_method('Tests/VanillaRecoveryWatchdogTests.cs', method, bump)

def close_failure(block):
    assert 'ArmFiveMinuteStall' in block
    block = block.replace('h.E.Seconds=1', 'h.E.Seconds=301', 1).replace('h.E.Seconds = 1', 'h.E.Seconds = 301', 1)
    block = block.replace('h.E.Seconds=2', 'h.E.Seconds=302', 1).replace('h.E.Seconds = 2', 'h.E.Seconds = 302', 1)
    block = block.replace('h.E.Seconds=3', 'h.E.Seconds=303', 1).replace('h.E.Seconds = 3', 'h.E.Seconds = 303', 1)
    return block
rewrite_method('Tests/VanillaRecoveryWatchdogTests.cs', 'CloseFailure', close_failure)

# In the mixed serialization case B's baseline is established after A reaches its
# five-minute terminal threshold. Advance another full ten minutes before asserting
# B's position restart, rather than only 299 seconds as the first transformed test did.
def mixed(block):
    assert 'h.ArmFiveMinuteStall(h.A,101)' in block
    assert 'h.E.Seconds=600' in block
    return block.replace('h.E.Seconds=600', 'h.E.Seconds=901', 1)
rewrite_method('Tests/VanillaRecoveryWatchdogTests.cs', 'Mixed', mixed)

print('Removed obsolete finite restart state and aligned recovery timing/cancellation regressions.')
