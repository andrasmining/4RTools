using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Net;
using _4RTools.Model.Vanilla;
using _4RTools.Model.Vanilla.Automation;

namespace _4RTools.Model
{

    public class ClientDTO
    {
        public int index { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public string hpAddress { get; set; }
        public string nameAddress { get; set; }

        public int hpAddressPointer { get; set; }
        public int nameAddressPointer { get; set; }

        public ClientDTO() { }

        public ClientDTO(string name, string description, string hpAddress, string nameAddress)
        {
            this.name= name;
            this.description = description;
            this.hpAddress = hpAddress;
            this.nameAddress = nameAddress;

            this.hpAddressPointer = Convert.ToInt32(hpAddress, 16);
            this.nameAddressPointer = Convert.ToInt32(nameAddress, 16);
        }

    }


    public sealed class ClientListSingleton
    {
        private static List<Client> clients = new List<Client>();
        
        public static void AddClient(Client c)
        {
            clients.Add(c);
        }

        public static void RemoveClient(Client c)
        {
            clients.Remove(c);
        }

        public static List<Client> GetAll()
        {
            return clients;
        }

        public static bool ExistsByProcessName(string processName)
        {
            return clients.Exists(client => client.processName == processName);
        }
    }

    public sealed class ClientSingleton
    {
        private static Client client;
        private ClientSingleton(Client client)
        {
            ClientSingleton.client = client;
        }

        public static ClientSingleton Instance(Client client)
        {
            return new ClientSingleton(client);
        }

        public static Client GetClient()
        {
            return client;
        }
    }

    public class Client : IDisposable
    {
        public Process process { get; }

        public string processName { get; private set; }
        private Utils.ProcessMemoryReader PMR { get; set; }
        public int currentNameAddress { get; set; }
        public int currentHPBaseAddress { get; set; }
        private int statusBufferAddress { get; set; }
        private int _num = 0;
        private VanillaStockBackend vanilla;
        private bool disposed;
        public bool IsVanilla { get { return IsVanillaProcessName(processName); } }
        public string VanillaCapabilities { get { return vanilla?.Capabilities; } }
        public string LastFailure { get { return vanilla?.LastFailure; } }

        public Client(string processName, int currentHPBaseAddress, int currentNameAddress)
        {
            this.currentNameAddress = currentNameAddress;
            this.currentHPBaseAddress = currentHPBaseAddress;
            this.processName = processName;
            this.statusBufferAddress = currentHPBaseAddress + 0x474;
        }

        public Client(ClientDTO dto)
        {
            this.processName = dto.name;
            this.currentHPBaseAddress = Convert.ToInt32(dto.hpAddress, 16);
            this.currentNameAddress = Convert.ToInt32(dto.nameAddress, 16);
            this.statusBufferAddress = this.currentHPBaseAddress + 0x474;
        }

        public Client(string processName) : this(processName, null) { }

        public Client(string processName, VanillaAutomationSession session)
        {
            string[] selection = processName.Split(new string[] { ".exe - " }, StringSplitOptions.None);
            int choosenPID;
            if (selection.Length != 2 || !int.TryParse(selection[1], out choosenPID) || choosenPID <= 0)
                throw new ArgumentException("Select a running Ragnarok client from the list.");
            string rawProcessName = selection[0];
            this.processName = rawProcessName;
            if (IsVanillaProcessName(rawProcessName))
            {
                if (session == null) throw new InvalidOperationException("The selected Vanilla client needs its shared read-only connection.");
                if (session.Snapshot == null || session.Snapshot.ProcessId != choosenPID) session.Connect(choosenPID);
                this.process = Process.GetProcessById(choosenPID);
                try
                {
                    if (!IsVanillaProcessName(this.process.ProcessName)) throw new InvalidOperationException("The selected process is no longer Vanilla MMO.");
                    vanilla = new VanillaStockBackend(() => session.Snapshot, () => session.IsObservationFresh,
                        permitted => session.CreateStockInput(permitted));
                }
                catch { this.process.Dispose(); throw; }
                return;
            }
            PMR = new Utils.ProcessMemoryReader();

            foreach (Process process in Process.GetProcessesByName(rawProcessName))
            {
                if (choosenPID == process.Id)
                {
                    this.process = process;
                    PMR.ReadProcess = process;
                    PMR.OpenProcess();

                    try
                    {
                        Client c = GetClientByProcess(rawProcessName);

                        if (c == null) throw new Exception();

                        this.currentHPBaseAddress = c.currentHPBaseAddress;
                        this.currentNameAddress = c.currentNameAddress;
                        this.statusBufferAddress = c.statusBufferAddress;
                    }catch
                    {
                        MessageBox.Show("This client is not supported. Only Spammers and macro will works.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        this.currentHPBaseAddress = 0;
                        this.currentNameAddress = 0;
                        this.statusBufferAddress = 0;
                    }
                   
                    //Do not block spammer for non supported Versions
                       
                }
            }
        }

        internal static bool IsVanillaProcessName(string name)
        {
            if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return string.Equals(name, "Vanilla MMO", StringComparison.OrdinalIgnoreCase);
        }

        private string ReadMemoryAsString(int address)
        {
            byte[] bytes = PMR.ReadProcessMemory((IntPtr)address, 40u, out _num);
            List<byte> buffer = new List<byte>(); //Need a list with dynamic size 
            for (int i =0;i < bytes.Length;i++)
            {
                if (bytes[i] == 0) break; //Check Nullability based ON ASCII Table

                buffer.Add(bytes[i]); //Add only bytes needed
            }

           return Encoding.Default.GetString(buffer.ToArray());

        }

        private uint ReadMemory(int address)
        {
            if (IsVanilla) throw new InvalidOperationException("Vanilla reads must use its validated read-only snapshot.");
            return BitConverter.ToUInt32(PMR.ReadProcessMemory((IntPtr)address, 4u, out _num), 0);
        }
        public void WriteMemory(int address, uint intToWrite)
        {
            if (IsVanilla) throw new InvalidOperationException("Game-memory writes are not supported for Vanilla.");
            PMR.WriteProcessMemory((IntPtr)address, BitConverter.GetBytes(intToWrite), out _num);
        }

        public void WriteMemory(int address, byte[] bytesToWrite)
        {
            if (IsVanilla) throw new InvalidOperationException("Game-memory writes are not supported for Vanilla.");
            PMR.WriteProcessMemory((IntPtr)address, bytesToWrite, out _num);
        }

        public bool IsHpBelow(int percent)
        {
            if (IsVanilla) return RequireVanilla().IsBelow(true, percent);
            return ReadCurrentHp() * 100 < percent * ReadMaxHp();
        }

        public bool IsSpBelow(int percent)
        {
            if (IsVanilla) return RequireVanilla().IsBelow(false, percent);
            return ReadCurrentSp() * 100 < percent * ReadMaxSp();
        }

        public uint ReadCurrentHp()
        {
            if (IsVanilla) return RequireVanilla().ReadVital(VanillaField.CurrentHP);
            return ReadMemory(this.currentHPBaseAddress);
        }

        public uint ReadCurrentSp()
        {
            if (IsVanilla) return RequireVanilla().ReadVital(VanillaField.CurrentSP);
            return ReadMemory(this.currentHPBaseAddress + 8);
        }

        public uint ReadMaxHp()
        {
            if (IsVanilla) return RequireVanilla().ReadVital(VanillaField.MaxHP);
            return ReadMemory(this.currentHPBaseAddress + 4);
        }

        public string ReadCharacterName()
        {
            if (IsVanilla)
            {
                string name;
                if (!RequireVanilla().TryReadCharacterName(out name)) throw new InvalidOperationException("Vanilla character name is unavailable or unverified.");
                return name;
            }
            return ReadMemoryAsString(this.currentNameAddress);
        }

        public uint ReadMaxSp()
        {
            if (IsVanilla) return RequireVanilla().ReadVital(VanillaField.MaxSP);
            return ReadMemory(this.currentHPBaseAddress + 12);
        }

        public uint CurrentBuffStatusCode(int effectStatusIndex)
        {
            if (IsVanilla)
            {
                uint[] statuses = RequireVanilla().ReadStatuses();
                if (effectStatusIndex < 0 || effectStatusIndex >= statuses.Length)
                    throw new InvalidOperationException("Status index is outside the verified Vanilla status snapshot.");
                return statuses[effectStatusIndex];
            }
            return ReadMemory(this.statusBufferAddress + effectStatusIndex * 4);
        }

        public uint[] ReadStatusSnapshot()
        {
            if (IsVanilla) return RequireVanilla().ReadStatuses();
            var result = new uint[Utils.Constants.MAX_BUFF_LIST_INDEX_SIZE];
            for (int index = 0; index < result.Length; index++) result[index] = CurrentBuffStatusCode(index);
            return result;
        }
        public bool TryReadCharacterName(out string name)
        {
            if (IsVanilla) return RequireVanilla().TryReadCharacterName(out name);
            name = ReadCharacterName(); return true;
        }
        public string GetEnableError(Profile profile) { return IsVanilla ? RequireVanilla().GetEnableError(profile) : null; }
        public void SetAutomationEnabled(bool enabled) { if (IsVanilla) RequireVanilla().SetEnabled(enabled); }
        public void EnsureAutomaticActionsReady() { if (IsVanilla) RequireVanilla().EnsureAutomaticReady(); }
        public bool HandleWorkerFailure(Exception error)
        {
            if (error is OperationCanceledException || Utils._4RThread.IsCancellationRequested) return true;
            if (!IsVanilla) return false;
            vanilla?.Fail(error.Message);
            return true;
        }
        public bool SendWindowMessage(IntPtr window, int message, Keys key, int data, bool automatic = false)
        {
            Utils._4RThread.ThrowIfCancellationRequested();
            if (!IsVanilla) return Utils.Interop.PostLegacyMessage(window, message, key, data);
            return RequireVanilla().SendWindowMessage(window, message, key, data, automatic);
        }
        private VanillaStockBackend RequireVanilla()
        {
            if (disposed || vanilla == null) throw new InvalidOperationException("Vanilla read-only connection is unavailable.");
            return vanilla;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { vanilla?.Dispose(); }
            finally
            {
                if (PMR != null)
                {
                    try { PMR.CloseHandle(); }
                    catch (Exception ex) { Console.Error.WriteLine("Client memory handle cleanup failed: " + ex.Message); }
                    PMR = null;
                }
                process?.Dispose();
            }
        }

        public Client GetClientByProcess(string processName)
        {
       
            foreach(Client c in ClientListSingleton.GetAll())
            {
                if (c.processName == processName)
                {
                    uint hpBaseValue = ReadMemory(c.currentHPBaseAddress);
                    if (hpBaseValue > 0) return c;
                }
            }
            return null;
        }
    
        public static Client FromDTO(ClientDTO dto)
        {
            return ClientListSingleton.GetAll()
                .Where(c => c.processName == dto.name)
                .Where(c => c.currentHPBaseAddress == dto.hpAddressPointer)
                .Where(c => c.currentNameAddress == dto.nameAddressPointer).FirstOrDefault();
        }
    }
}
