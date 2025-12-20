namespace AutoCadMcpServer.Core
{
    public static class AtomicFile
    {
        public static void WriteReplace(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            int delay = 100;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        try { File.Replace(tmp, path, null, true); return; }
                        catch { TryDelete(path); File.Move(tmp, path); return; }
                    }
                    else { File.Move(tmp, path); return; }
                }
                catch { Thread.Sleep(delay); delay = Math.Min(delay * 2, 2000); }
            }
            TryDelete(tmp);
            throw new IOException("E_ATOMIC_WRITE: Atomic replace failed: " + path);
        }

        public static void AtomicMove(string src, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            int delay = 100;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    if (File.Exists(dest))
                    {
                        try { File.Replace(src, dest, null, true); return; }
                        catch { TryDelete(dest); File.Move(src, dest); return; }
                    }
                    else { File.Move(src, dest); return; }
                }
                catch { Thread.Sleep(delay); delay = Math.Min(delay * 2, 2000); }
            }
            throw new IOException("E_ATOMIC_WRITE: Atomic move failed: " + dest);
        }

        private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    }
}

