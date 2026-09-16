from pathlib import Path

def edit(name, old, new):
    p=Path(name)
    raw=p.read_bytes()
    text=raw.decode().replace('\r\n','\n')
    assert text.count(old)==1,(name,old[:80],text.count(old))
    text=text.replace(old,new)
    p.write_bytes(text.replace('\n','\r\n').encode() if b'\r\n' in raw else text.encode())

edit('Model/Vanilla/VanillaReconnect.cs','''            return Process.GetProcessesByName("Vanilla MMO").Where(p =>
            {
                try { return !p.HasExited; }
                catch { p.Dispose(); return false; }
            }).ToList();''','''            // The process-list snapshot is presence evidence. A protected metadata
            // query failure must not remove a live PID and create a false free slot.
            return Process.GetProcessesByName("Vanilla MMO").ToList();''')
edit('Model/Vanilla/VanillaStartupOrchestrator.cs','''            try { using (var process = Process.GetProcessById(pid)) return !process.HasExited; }
            catch { return false; }''','''            var processes = Process.GetProcessesByName("Vanilla MMO");
            try { return processes.Any(process => process.Id == pid); }
            finally { foreach (var process in processes) process.Dispose(); }
            // Enumeration failure propagates; it is not a missing client.''')
edit('Model/Vanilla/VanillaReconnect.cs','            public DateTimeOffset? LastPopup;\n','')
edit('RELEASE-NOTES.md','Missing processes keep the existing immediate sequential relaunch behavior.','''Missing processes keep the existing immediate sequential relaunch behavior.
Process presence comes from the process-list snapshot: a denied metadata query
cannot silently discard a live PID or authorize a duplicate replacement.''')
print('Process presence remains distinct from unavailable metadata; obsolete popup field removed.')
