namespace HoneyGrid.Sensors.NodeMetrics;

/// <summary>Migawka licznika CPU z /proc/stat (jiffies).</summary>
public readonly record struct CpuSample(ulong Idle, ulong Total);

/// <summary>
/// Czyste, bezstanowe odczyty z /proc (Linux). Wszystko defensywne: błąd/parse →
/// wartość neutralna (0 / null), bo agent nie może wywrócić się na nietypowym /proc
/// (np. uruchomienie poza Linuksem — wtedy metryki = 0, bez wyjątku).
/// </summary>
public static class ProcReader
{
    /// <summary>Sumaryczny licznik CPU (idle vs total). CPU% liczy się z różnicy dwóch migawek.</summary>
    public static CpuSample? ReadCpu(string procPath)
    {
        try
        {
            var line = File.ReadLines(Path.Combine(procPath, "stat"))
                .FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
            if (line is null) return null;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // parts: "cpu" user nice system idle iowait irq softirq steal ...
            ulong total = 0;
            ulong idle = 0;
            for (var i = 1; i < parts.Length; i++)
            {
                if (!ulong.TryParse(parts[i], out var v)) continue;
                total += v;
                if (i == 4) idle += v;  // idle
                if (i == 5) idle += v;  // iowait (też traktujemy jak bezczynność)
            }
            return total == 0 ? null : new CpuSample(idle, total);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Zużycie RAM w % = (MemTotal − MemAvailable) / MemTotal.</summary>
    public static int ReadMemoryPercent(string procPath)
    {
        try
        {
            ulong memTotal = 0, memAvail = 0;
            foreach (var l in File.ReadLines(Path.Combine(procPath, "meminfo")))
            {
                if (l.StartsWith("MemTotal:", StringComparison.Ordinal)) memTotal = ParseKb(l);
                else if (l.StartsWith("MemAvailable:", StringComparison.Ordinal)) memAvail = ParseKb(l);
                if (memTotal > 0 && memAvail > 0) break;
            }
            if (memTotal == 0) return 0;
            var used = memTotal > memAvail ? memTotal - memAvail : 0;
            return (int)Math.Round(100.0 * used / memTotal);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Suma bajtów RX+TX po wszystkich interfejsach (poza lo) z /proc/net/dev.</summary>
    public static ulong ReadNetBytes(string procPath)
    {
        try
        {
            ulong bytes = 0;
            foreach (var l in File.ReadLines(Path.Combine(procPath, "net", "dev")))
            {
                var idx = l.IndexOf(':');
                if (idx < 0) continue;
                if (l[..idx].Trim() == "lo") continue;
                var nums = l[(idx + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // kolumny: rxBytes rxPackets ... (8 pól) txBytes ...
                if (nums.Length > 8)
                {
                    if (ulong.TryParse(nums[0], out var rx)) bytes += rx;
                    if (ulong.TryParse(nums[8], out var tx)) bytes += tx;
                }
            }
            return bytes;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Liczba połączeń TCP w stanie ESTABLISHED (st == "01") z /proc/net/tcp{,6}.</summary>
    public static int ReadEstablishedConnections(string procPath)
    {
        var count = 0;
        foreach (var file in new[] { "tcp", "tcp6" })
        {
            try
            {
                var path = Path.Combine(procPath, "net", file);
                if (!File.Exists(path)) continue;
                var first = true;
                foreach (var l in File.ReadLines(path))
                {
                    if (first) { first = false; continue; } // nagłówek
                    var parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 3 && parts[3] == "01") count++;
                }
            }
            catch
            {
                // ignorujemy pojedynczy plik
            }
        }
        return count;
    }

    private static ulong ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 && ulong.TryParse(parts[1], out var v) ? v : 0;
    }
}
