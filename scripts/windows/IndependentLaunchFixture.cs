using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml;

[assembly: AssemblyTitle("Yarnelo independent-launch test fixture")]
[assembly: AssemblyProduct("Yarnelo")]

// Disposable processes only. The harness owns one new unnamed nested job; it
// never opens, changes or closes the automation host's job or any real dock.
internal static class IndependentLaunchFixture {
    const uint KillOnClose=0x2000, Suspended=4, NoWindow=0x08000000, WaitTimeout=258;
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits {
        internal long ProcessTime,JobTime; internal uint Flags;
        internal UIntPtr MinWorkingSet,MaxWorkingSet; internal uint ActiveProcesses;
        internal UIntPtr Affinity; internal uint Priority,Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { internal ulong A,B,C,D,E,F; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits {
        internal BasicLimits Basic; internal IoCounters Io;
        internal UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartupInfo {
        internal uint Size; internal string Reserved,Desktop,Title;
        internal uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;
        internal ushort ShowWindow,ReservedCount; internal IntPtr ReservedBytes,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { internal IntPtr Process,Thread; internal uint Pid,Tid; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int kind,ref ExtendedLimits data,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool member);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string executable,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr handle,uint exitCode);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int kind,out int value,int size,out int returned);

    static void Check(bool value,string message) { if(!value) throw new InvalidOperationException(message+" (Win32="+Marshal.GetLastWin32Error()+")"); }
    static string Exe { get { return Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName); } }
    static string Sid { get { using(var identity=WindowsIdentity.GetCurrent()) return identity.User.Value; } }
    static string CheckedRoot(string root,string token) {
        Guid parsed;
        Check(Guid.TryParseExact(token,"N",out parsed),"Invalid fixture token");
        root=Path.GetFullPath(root);
        Check(Path.GetFileName(root)=="yarnelo-independent-test-"+token,"Unexpected fixture directory");
        for(string p=root; !String.IsNullOrEmpty(p); p=Path.GetDirectoryName(p))
            if(File.Exists(p)||Directory.Exists(p)) Check((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"Redirected fixture directory");
        string[] marker=File.ReadAllLines(Path.Combine(root,"fixture-token.txt"),Encoding.UTF8);
        Check(marker.Length==3 && marker[0]==token && marker[1]==root && marker[2]==Sid,"Fixture marker does not match this user");
        string leaf=Path.GetFileName(Exe);
        Check(String.Equals(Path.GetDirectoryName(Exe),root,StringComparison.OrdinalIgnoreCase) &&
            (leaf=="Yarnelo.exe" || leaf=="DirectControl.exe" || leaf=="LifetimeHarness.exe"),"Only the isolated fixture executables are allowed");
        return root;
    }
    internal static string Quote(string value) {
        var output=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c=='\\') { slashes++; continue; }
            if(c=='\"') output.Append('\\',slashes*2+1).Append(c);
            else output.Append('\\',slashes).Append(c);
            slashes=0;
        }
        return output.Append('\\',slashes*2).Append('"').ToString();
    }
    static void AtomicWrite(string path,string text) {
        string pending=path+".pending";
        File.WriteAllText(pending,text,new UTF8Encoding(false));
        for(int attempt=0;;attempt++) {
            try { if(File.Exists(path)) File.Replace(pending,path,null); else File.Move(pending,path); return; }
            catch(IOException) { if(attempt>=20) throw; Thread.Sleep(10); }
        }
    }
    static int Sentinel(string root,string token,string mode,string[] args) {
        root=CheckedRoot(root,token);
        Check(mode=="direct"||mode=="scheduled","Unexpected sentinel mode");
        Check(Path.GetFileName(Exe)==(mode=="scheduled"?"Yarnelo.exe":"DirectControl.exe"),"Sentinel has the wrong fixture executable");
        IntPtr handle; Check(OpenProcessToken(Process.GetCurrentProcess().Handle,8,out handle),"Cannot read fixture token");
        int elevated,returned;
        try { Check(GetTokenInformation(handle,20,out elevated,4,out returned),"Cannot read token elevation"); }
        finally { CloseHandle(handle); }
        var document=new XmlDocument(); var receipt=document.CreateElement("Receipt"); document.AppendChild(receipt);
        Action<string,string> add=delegate(string name,string value) { var e=document.CreateElement(name); e.InnerText=value; receipt.AppendChild(e); };
        add("Token",token); add("ProcessId",Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
        add("Executable",Exe); add("Directory",Path.GetFullPath(Environment.CurrentDirectory)); add("UserSid",Sid);
        add("SessionId",Process.GetCurrentProcess().SessionId.ToString(CultureInfo.InvariantCulture)); add("Elevated",elevated.ToString(CultureInfo.InvariantCulture));
        var arguments=document.CreateElement("Arguments"); receipt.AppendChild(arguments);
        for(int i=4;i<args.Length;i++) { var a=document.CreateElement("Argument"); a.InnerText=args[i]; arguments.AppendChild(a); }
        AtomicWrite(Path.Combine(root,mode+"-receipt.xml"),document.OuterXml);
        var lifetime=Stopwatch.StartNew(); int count=0;
        while(lifetime.Elapsed.TotalSeconds<45) {
            string stop=Path.Combine(root,"stop-"+mode+".txt");
            if(File.Exists(stop) && File.ReadAllText(stop,Encoding.UTF8)==token) return 0;
            AtomicWrite(Path.Combine(root,mode+"-heartbeat.txt"),(++count).ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(100);
        }
        return 0;
    }
    static XmlDocument WaitXml(string path,Stopwatch watch) {
        while(watch.Elapsed.TotalSeconds<30) {
            if(File.Exists(path)) { var xml=new XmlDocument {XmlResolver=null}; xml.Load(path); return xml; }
            string callerError=Path.Combine(Path.GetDirectoryName(path),"caller-error.txt");
            if(File.Exists(callerError)) throw new InvalidOperationException("Disposable caller failed: "+File.ReadAllText(callerError,Encoding.UTF8));
            Thread.Sleep(100);
        }
        throw new TimeoutException("Fixture receipt missing: "+Path.GetFileName(path));
    }
    static string Value(XmlDocument document,string name) { return document.DocumentElement[name].InnerText; }
    static Process ValidateReceipt(XmlDocument receipt,string root,string token,bool scheduled) {
        string executable=Path.Combine(root,scheduled?"Yarnelo.exe":"DirectControl.exe");
        Check(Value(receipt,"Token")==token && String.Equals(Value(receipt,"Executable"),executable,StringComparison.OrdinalIgnoreCase) &&
            String.Equals(Value(receipt,"Directory"),root,StringComparison.OrdinalIgnoreCase) && Value(receipt,"UserSid")==Sid &&
            Value(receipt,"SessionId")==Process.GetCurrentProcess().SessionId.ToString(CultureInfo.InvariantCulture),"Wrong sentinel identity/directory/session");
        if(scheduled) Check(Value(receipt,"Elevated")=="0","The scheduled sentinel must run without elevation");
        var process=Process.GetProcessById(Int32.Parse(Value(receipt,"ProcessId"),CultureInfo.InvariantCulture));
        Check(String.Equals(process.MainModule.FileName,executable,StringComparison.OrdinalIgnoreCase),"Receipt PID targets another executable");
        return process;
    }
    static bool MissingTask(Exception error) {
        while(error.InnerException!=null) error=error.InnerException;
        return error.HResult==unchecked((int)0x80070002)||error.HResult==unchecked((int)0x8004130f);
    }
    static void VerifyTaskAbsent(string name) {
        Check(name.StartsWith("Yarnelo.Launch.",StringComparison.Ordinal) && name.IndexOfAny(new[]{'\\','/'})<0,"Unexpected temporary task identity");
        object service=null,folder=null,task=null;
        try {
            service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            service.GetType().InvokeMember("Connect",BindingFlags.InvokeMethod,null,service,new object[0]);
            folder=service.GetType().InvokeMember("GetFolder",BindingFlags.InvokeMethod,null,service,new object[]{"\\"});
            try { task=folder.GetType().InvokeMember("GetTask",BindingFlags.InvokeMethod,null,folder,new object[]{name}); }
            catch(Exception e) { if(MissingTask(e)) return; throw; }
            throw new InvalidOperationException("The temporary demand-only task still exists");
        } finally {
            foreach(object value in new[]{task,folder,service}) if(value!=null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
    static int Harness(string root,string token,string caller,string shell,string helper) {
        root=CheckedRoot(root,token);
        Check(Path.GetFileName(Exe)=="LifetimeHarness.exe","Wrong native harness executable");
        IntPtr job=IntPtr.Zero; ProcessInfo child=new ProcessInfo(); Process direct=null,scheduled=null; bool assigned=false;
        try {
            job=CreateJobObject(IntPtr.Zero,null); Check(job!=IntPtr.Zero,"Cannot create disposable job");
            var limits=new ExtendedLimits(); limits.Basic.Flags=KillOnClose;
            Check(SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(ExtendedLimits))),"Cannot configure disposable job");
            string command=Quote(shell)+" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(caller)+" -FixtureRoot "+Quote(root)+" -Token "+Quote(token)+" -Helper "+Quote(helper);
            var startup=new StartupInfo(); startup.Size=(uint)Marshal.SizeOf(typeof(StartupInfo));
            Check(CreateProcess(shell,new StringBuilder(command),IntPtr.Zero,IntPtr.Zero,false,Suspended|NoWindow,IntPtr.Zero,root,ref startup,out child),"Cannot create disposable caller");
            Check(AssignProcessToJobObject(job,child.Process),"Cannot assign disposable caller to its new job"); assigned=true;
            Check(ResumeThread(child.Thread)!=UInt32.MaxValue,"Cannot resume disposable caller");
            var watch=Stopwatch.StartNew();
            direct=ValidateReceipt(WaitXml(Path.Combine(root,"direct-receipt.xml"),watch),root,token,false);
            scheduled=ValidateReceipt(WaitXml(Path.Combine(root,"scheduled-receipt.xml"),watch),root,token,true);
            var result=WaitXml(Path.Combine(root,"caller-result.xml"),watch);
            Check(Value(result,"ProcessId")==scheduled.Id.ToString(CultureInfo.InvariantCulture),"Helper returned a different process");
            Check(Value(result,"TemporaryTaskRemoved")=="True","Helper did not verify temporary task cleanup");
            VerifyTaskAbsent(Value(result,"TemporaryTaskName"));
            bool member;
            Check(IsProcessInJob(child.Process,job,out member)&&member,"Caller is not in the disposable job");
            Check(IsProcessInJob(direct.Handle,job,out member)&&member,"Direct control did not inherit disposable job");
            Check(IsProcessInJob(scheduled.Handle,job,out member)&&!member,"Scheduled child inherited disposable job");
            int before=Int32.Parse(File.ReadAllText(Path.Combine(root,"scheduled-heartbeat.txt")),CultureInfo.InvariantCulture);
            Check(CloseHandle(job),"Cannot close disposable job"); job=IntPtr.Zero;
            Check(WaitForSingleObject(child.Process,5000)!=WaitTimeout,"Disposable caller survived kill-on-close");
            Check(direct.WaitForExit(5000),"Direct control survived kill-on-close");
            Thread.Sleep(2600);
            Check(!scheduled.HasExited,"Scheduled child exited when disposable job closed");
            int after=Int32.Parse(File.ReadAllText(Path.Combine(root,"scheduled-heartbeat.txt")),CultureInfo.InvariantCulture);
            Check(after>=before+15,"Scheduled heartbeat did not advance after disposable job closed");
            AtomicWrite(Path.Combine(root,"harness-result.txt"),"Passed\nCaller/control exited; scheduled sentinel survived and advanced heartbeat after temporary task removal.\n");
            Console.WriteLine("PASS: independent scheduled process survived disposal of only the isolated caller job.");
            return 0;
        } finally {
            if(job!=IntPtr.Zero) CloseHandle(job);
            // Assignment failure leaves only our newly created suspended caller.
            // Its exact creation handle is the sole permitted termination target.
            if(!assigned && child.Process!=IntPtr.Zero) TerminateProcess(child.Process,97);
            // Only token-authenticated fixture sentinels observe these stop files.
            foreach(string mode in new[]{"direct","scheduled"}) AtomicWrite(Path.Combine(root,"stop-"+mode+".txt"),token);
            if(scheduled!=null) {
                try { Check(scheduled.WaitForExit(5000),"Scheduled fixture did not stop after its authenticated cleanup request"); }
                finally { scheduled.Dispose(); }
            }
            if(direct!=null) direct.Dispose();
            if(child.Thread!=IntPtr.Zero) CloseHandle(child.Thread);
            if(child.Process!=IntPtr.Zero) CloseHandle(child.Process);
        }
    }
    static int Main(string[] args) {
        try {
            if(args.Length>=4 && args[0]=="--sentinel") return Sentinel(args[1],args[2],args[3],args);
            if(args.Length==6 && args[0]=="--harness") return Harness(args[1],args[2],args[3],args[4],args[5]);
            return 80;
        } catch(Exception error) {
            if(args.Length>=3) {
                try { string root=CheckedRoot(args[1],args[2]); AtomicWrite(Path.Combine(root,"fixture-error.txt"),error.ToString()); } catch { }
            }
            Console.Error.WriteLine(error); return 90;
        }
    }
}
