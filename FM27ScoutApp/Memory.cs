// Lecture (seule) de la mémoire du processus FM27 (fm.exe), depuis l'extérieur.
// Même principe que FMRTE / Genie Scout : OpenProcess + ReadProcessMemory.
// Écriture : uniquement via Write(), appelée par « Appliquer dans le jeu » après confirmation, sur des réglages calibrés.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace FM27Scout
{
    internal sealed class GameProcess : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(nint h);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(nint h, ulong addr, byte[] buf, nuint size, out nuint read);
        [DllImport("kernel32.dll")] static extern ulong VirtualQueryEx(nint h, ulong addr, out MBI info, ulong len);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(nint h, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(nint h, ulong addr, byte[] buf, nuint size, out nuint written);

        [StructLayout(LayoutKind.Sequential)]
        struct MBI
        {
            public ulong BaseAddress, AllocationBase;
            public uint AllocationProtect, _a1;
            public ulong RegionSize;
            public uint State, Protect, Type, _a2;
        }

        const uint PROCESS_VM_READ = 0x0010, PROCESS_QUERY_INFORMATION = 0x0400, PROCESS_VM_WRITE = 0x0020, PROCESS_VM_OPERATION = 0x0008;
        nint _hw;   // poignée d'écriture, ouverte seulement au premier « Appliquer »
        const uint MEM_COMMIT = 0x1000, MEM_PRIVATE = 0x20000, PAGE_READWRITE = 0x04;

        public readonly int Pid;
        readonly nint _h;
        public ulong PluginBase, PluginEnd;          // game_plugin.dll (le moteur natif)
        public string PluginVersion = "";

        GameProcess(Process p, nint h)
        {
            Pid = p.Id; _h = h;
            foreach (ProcessModule m in p.Modules)
            {
                if (!m.ModuleName.Equals("game_plugin.dll", StringComparison.OrdinalIgnoreCase)) continue;
                PluginBase = (ulong)m.BaseAddress;
                PluginEnd = PluginBase + (ulong)m.ModuleMemorySize;
                try { PluginVersion = FileVersionInfo.GetVersionInfo(m.FileName).FileVersion ?? ""; } catch { }
            }
        }

        /// <summary>Cherche FM27 (fm.exe) en cours d'exécution. null si absent ou inaccessible.</summary>
        public static GameProcess Find(out string why)
        {
            why = "";
            var p = Process.GetProcessesByName("fm").FirstOrDefault();
            if (p == null) { why = L.T("FM27 n'est pas lancé."); return null; }
            nint h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, p.Id);
            if (h == 0) { why = L.T("Accès refusé à FM27 (lance l'app avec le même compte que le jeu)."); return null; }
            try
            {
                var g = new GameProcess(p, h);
                if (g.PluginBase == 0) { g.Dispose(); why = L.T("FM27 démarre encore…"); return null; }
                return g;
            }
            catch (Exception e) { CloseHandle(h); why = L.F("FM27 démarre encore… ({0})", e.Message); return null; }
        }

        public bool Alive => GetExitCodeProcess(_h, out uint c) && c == 259;   // STILL_ACTIVE

        public bool Read(ulong addr, byte[] buf, int len) =>
            addr >= 0x10000 && ReadProcessMemory(_h, addr, buf, (nuint)len, out var n) && (int)n == len;

        [ThreadStatic] static byte[] t_small;

        public ulong Ptr(ulong addr)
        {
            t_small ??= new byte[256];
            return Read(addr, t_small, 8) ? BitConverter.ToUInt64(t_small, 0) : 0;
        }

        public uint U32(ulong addr)
        {
            t_small ??= new byte[256];
            return Read(addr, t_small, 4) ? BitConverter.ToUInt32(t_small, 0) : 0xFFFFFFFF;
        }

        /// <summary>Chaîne C UTF-8 (FM stocke ses textes à ptr+4).</summary>
        public string CStr(ulong addr)
        {
            t_small ??= new byte[256];
            if (!Read(addr, t_small, 128)) return null;
            int n = Array.IndexOf(t_small, (byte)0, 0, 128);
            if (n <= 0) return null;
            var s = System.Text.Encoding.UTF8.GetString(t_small, 0, n);
            return s.Length <= 60 && s.All(c => !char.IsControl(c)) ? s : null;
        }

        /// <summary>Zones de données du jeu (tas : privé, alloué, lecture-écriture).</summary>
        public List<(ulong start, ulong size)> Regions()
        {
            var list = new List<(ulong, ulong)>();
            ulong addr = 0x10000;
            int sz = Marshal.SizeOf<MBI>();
            while (addr < 0x7FFF_FFFF_0000)
            {
                if (VirtualQueryEx(_h, addr, out var m, (ulong)sz) == 0 || m.RegionSize == 0) break;
                if (m.State == MEM_COMMIT && m.Type == MEM_PRIVATE && m.Protect == PAGE_READWRITE && m.RegionSize <= 1UL << 30)
                    list.Add((m.BaseAddress, m.RegionSize));
                addr = m.BaseAddress + m.RegionSize;
            }
            return list;
        }

        /// <summary>Zones de données modifiables du moteur (game_plugin.dll : variables globales), pour les chemins « statiques ».</summary>
        public List<(ulong start, ulong size)> ModuleDataRegions()
        {
            var list = new List<(ulong, ulong)>();
            ulong addr = PluginBase;
            int sz = Marshal.SizeOf<MBI>();
            while (addr < PluginEnd)
            {
                if (VirtualQueryEx(_h, addr, out var m, (ulong)sz) == 0 || m.RegionSize == 0) break;
                if (m.State == MEM_COMMIT && (m.Protect == PAGE_READWRITE || m.Protect == 0x08)) list.Add((m.BaseAddress, m.RegionSize));
                addr = m.BaseAddress + m.RegionSize;
            }
            return list;
        }

        /// <summary>Écrit quelques octets (réglage calibré) puis relit pour vérifier.</summary>
        public bool Write(ulong addr, byte[] data)
        {
            if (addr < 0x10000 || data.Length == 0 || data.Length > 16) return false;
            if (_hw == 0) _hw = OpenProcess(PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_VM_READ, false, Pid);
            if (_hw == 0) return false;
            if (!WriteProcessMemory(_hw, addr, data, (nuint)data.Length, out var n) || (int)n != data.Length) return false;
            var check = new byte[data.Length];
            return Read(addr, check, check.Length) && check.AsSpan().SequenceEqual(data);
        }

        public void Dispose() { CloseHandle(_h); if (_hw != 0) CloseHandle(_hw); }
    }
}
