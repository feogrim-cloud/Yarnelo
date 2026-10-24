using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;
using Microsoft.Win32;

namespace Polka {
    // Keep this independent from WPF and app resources: uninstall uses the same
    // ownership checks without loading or modifying the shortcut profile.
    internal sealed class StartupRegistrationException : IOException {
        internal readonly string MessageKey;
        internal StartupRegistrationException(string key,string message) : base(message) { MessageKey=key; }
        internal StartupRegistrationException(string key,string message,Exception inner) : base(message,inner) { MessageKey=key; }
    }

    internal sealed class StartupRegistration {
        internal const string SourceMarker="Yarnelo.Startup.v1";
        internal const string TaskNamespace="http://schemas.microsoft.com/windows/2004/02/mit/task";
        const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        static readonly string[] LegacyNames={"Yarnelo","Polka Desktop Dock"};
        readonly string executable, userSid;
        readonly bool manageLegacy;
        readonly string taskName;

        internal string TaskName { get { return taskName; } }
        internal StartupRegistration(string executablePath) : this(executablePath,null,true) { }
        // Tests may use a fresh task name, but can never touch production Run
        // entries through this constructor unless explicitly requested.
        internal StartupRegistration(string executablePath,string isolatedTaskName,bool manageLegacy) {
            executable=AbsolutePath(executablePath);
            using(var identity=WindowsIdentity.GetCurrent()) {
                if(identity.User==null) throw new StartupRegistrationException("StartupTaskUnavailable","Could not identify the signed-in user.");
                userSid=identity.User.Value;
            }
            if(isolatedTaskName!=null && (!isolatedTaskName.StartsWith("Yarnelo.Startup.Test.",StringComparison.Ordinal) ||
                isolatedTaskName.Length>150 || isolatedTaskName.IndexOfAny(new[]{'\\','/','\0'})>=0))
                throw new ArgumentException("A test task needs an isolated Yarnelo.Startup.Test. name.","isolatedTaskName");
            taskName=isolatedTaskName ?? "Yarnelo.Logon."+userSid;
            this.manageLegacy=manageLegacy;
        }

        internal enum StartupState { Missing, Enabled, Disabled, Foreign }
        internal bool Enabled { get { return State==StartupState.Enabled; } }
        internal StartupState State {
            get {
                using(var tasks=new TaskConnection()) {
                    object task=tasks.Find(taskName);
                    try {
                        if(task!=null) {
                            bool xmlEnabled;
                            if(!IsOwnedTaskXml((string)ComGet(task,"Xml"),executable,userSid,out xmlEnabled)) return StartupState.Foreign;
                            return xmlEnabled && Convert.ToBoolean(ComGet(task,"Enabled"),CultureInfo.InvariantCulture) ? StartupState.Enabled : StartupState.Disabled;
                        }
                    } finally { Release(task); }
                }
                return manageLegacy ? ReadLegacyState() : StartupState.Missing;
            }
        }

        internal void SetEnabled(bool enabled) {
            if(enabled && !File.Exists(executable)) throw new StartupRegistrationException("StartupExecutableMissing","The Yarnelo executable could not be found.");
            using(var tasks=new TaskConnection()) {
                object task=tasks.Find(taskName);
                bool owned=false;
                try {
                    bool ignored;
                    owned=task!=null && IsOwnedTaskXml((string)ComGet(task,"Xml"),executable,userSid,out ignored);
                    if(enabled) {
                        if(task!=null && !owned) throw new StartupRegistrationException("StartupTaskForeign","A task with this name belongs to another program or installation.");
                        object registered=null;
                        try {
                            // CREATE for a new task fails safely if another task
                            // appeared after our lookup. Never use an unconditional
                            // CREATE_OR_UPDATE against an unowned task name.
                            registered=ComCall(tasks.Folder,"RegisterTask",taskName,CreateTaskXml(executable,userSid,true),task==null?2:4,userSid,null,3,null);
                        } finally { Release(registered); }
                        object saved=tasks.Find(taskName);
                        try {
                            bool savedEnabled;
                            if(saved==null || !IsOwnedTaskXml((string)ComGet(saved,"Xml"),executable,userSid,out savedEnabled) ||
                                !savedEnabled || !Convert.ToBoolean(ComGet(saved,"Enabled"),CultureInfo.InvariantCulture))
                                throw new StartupRegistrationException("StartupTaskVerifyFailed","The saved startup task could not be verified.");
                        } finally { Release(saved); }
                    } else if(owned) {
                        ComCall(tasks.Folder,"DeleteTask",taskName,0);
                        object remaining=tasks.Find(taskName);
                        try {
                            bool remainingEnabled;
                            if(remaining!=null && IsOwnedTaskXml((string)ComGet(remaining,"Xml"),executable,userSid,out remainingEnabled))
                                throw new StartupRegistrationException("StartupTaskVerifyFailed","The startup task could not be removed.");
                        } finally { Release(remaining); }
                    }
                } finally { Release(task); }
            }
            // A verified task replaces only Run commands for this exact binary.
            // Approval records are left alone: stale entries cannot establish
            // ownership and may describe a different portable installation.
            if(manageLegacy) RemoveOwnedLegacy();
        }

        internal bool MigrateEnabledLegacy() {
            if(!manageLegacy) return false;
            bool existingOwnedEnabled=false;
            using(var tasks=new TaskConnection()) {
                object task=tasks.Find(taskName);
                try {
                    if(task!=null) {
                        bool xmlEnabled;
                        if(!IsOwnedTaskXml((string)ComGet(task,"Xml"),executable,userSid,out xmlEnabled) || !xmlEnabled ||
                            !Convert.ToBoolean(ComGet(task,"Enabled"),CultureInfo.InvariantCulture)) return false;
                        existingOwnedEnabled=true;
                    }
                } finally { Release(task); }
            }
            if(ReadLegacyState()!=StartupState.Enabled) return false;
            // A previous registration may have succeeded before readback or
            // Run cleanup failed. Finish that transaction without rewriting
            // the existing task or changing a disabled Windows preference.
            if(existingOwnedEnabled) RemoveOwnedLegacy();
            else SetEnabled(true);
            return true;
        }

        internal static void RemoveOwned(string executablePath) {
            new StartupRegistration(executablePath).SetEnabled(false);
        }

        StartupState ReadLegacyState() {
            bool found=false;
            foreach(RegistryView view in RegistryViews())
            using(RegistryKey user=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view))
            using(RegistryKey run=user.OpenSubKey(RunKey,false)) {
                if(run==null) continue;
                foreach(string name in LegacyNames) {
                    string command=run.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if(!IsOwnedRunCommand(command,executable)) continue;
                    found=true;
                    foreach(string branch in new[]{"Run","Run32"})
                    using(RegistryKey approved=user.OpenSubKey(ApprovedKey+branch,false)) {
                        object state=approved==null?null:approved.GetValue(name);
                        if(!ApprovalAllowsStartup(state)) return StartupState.Disabled;
                    }
                }
            }
            return found?StartupState.Enabled:StartupState.Missing;
        }

        void RemoveOwnedLegacy() {
            foreach(RegistryView view in RegistryViews())
            using(RegistryKey user=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view))
            using(RegistryKey run=user.OpenSubKey(RunKey,true)) {
                if(run==null) continue;
                foreach(string name in LegacyNames) {
                    string command=run.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if(IsOwnedRunCommand(command,executable)) {
                        run.DeleteValue(name,false);
                        if(run.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames)!=null)
                            throw new StartupRegistrationException("StartupTaskVerifyFailed","The replaced Run registration could not be removed.");
                    }
                }
            }
        }

        static RegistryView[] RegistryViews() {
            return Environment.Is64BitOperatingSystem ? new[]{RegistryView.Registry64,RegistryView.Registry32} : new[]{RegistryView.Registry32};
        }

        internal static bool ApprovalAllowsStartup(object value) {
            if(value==null) return true;
            byte[] bytes=value as byte[];
            if(bytes==null || bytes.Length<4) return false;
            uint state=BitConverter.ToUInt32(bytes,0);
            return state==2 || state==6;
        }

        internal static bool IsOwnedRunCommand(string command,string executablePath) {
            if(command==null) return false;
            string normalized;
            try { normalized=AbsolutePath(executablePath); } catch(ArgumentException) { return false; }
            command=command.Trim();
            return String.Equals(command,normalized,StringComparison.OrdinalIgnoreCase) ||
                String.Equals(command,"\""+normalized+"\"",StringComparison.OrdinalIgnoreCase) ||
                String.Equals(command,"\""+normalized+"\" --startup",StringComparison.OrdinalIgnoreCase);
        }

        internal static string CreateTaskXml(string executablePath,string sid,bool enabled) {
            string path=AbsolutePath(executablePath);
            if(String.IsNullOrWhiteSpace(sid)) throw new ArgumentException("A user SID is required.","sid");
            var document=new XmlDocument { XmlResolver=null };
            var task=document.CreateElement("Task",TaskNamespace); task.SetAttribute("version","1.2"); document.AppendChild(task);
            var registration=Add(document,task,"RegistrationInfo",null);
            Add(document,registration,"Author",sid);
            Add(document,registration,"Description","Start Yarnelo after this user signs in.");
            Add(document,registration,"Source",SourceMarker);
            var triggers=Add(document,task,"Triggers",null);
            var trigger=Add(document,triggers,"LogonTrigger",null);
            Add(document,trigger,"Enabled","true"); Add(document,trigger,"UserId",sid); Add(document,trigger,"Delay","PT10S");
            var principals=Add(document,task,"Principals",null);
            var principal=Add(document,principals,"Principal",null); principal.SetAttribute("id","Author");
            Add(document,principal,"UserId",sid); Add(document,principal,"LogonType","InteractiveToken"); Add(document,principal,"RunLevel","LeastPrivilege");
            var settings=Add(document,task,"Settings",null);
            Add(document,settings,"MultipleInstancesPolicy","IgnoreNew");
            Add(document,settings,"DisallowStartIfOnBatteries","false"); Add(document,settings,"StopIfGoingOnBatteries","false");
            Add(document,settings,"AllowHardTerminate","false"); Add(document,settings,"StartWhenAvailable","true");
            Add(document,settings,"RunOnlyIfNetworkAvailable","false");
            var idle=Add(document,settings,"IdleSettings",null);
            Add(document,idle,"StopOnIdleEnd","false"); Add(document,idle,"RestartOnIdle","false");
            Add(document,settings,"AllowStartOnDemand","true"); Add(document,settings,"Enabled",enabled?"true":"false");
            Add(document,settings,"Hidden","false"); Add(document,settings,"RunOnlyIfIdle","false");
            Add(document,settings,"WakeToRun","false"); Add(document,settings,"ExecutionTimeLimit","PT0S"); Add(document,settings,"Priority","7");
            var actions=Add(document,task,"Actions",null); actions.SetAttribute("Context","Author");
            var action=Add(document,actions,"Exec",null);
            Add(document,action,"Command",path); Add(document,action,"Arguments","--startup"); Add(document,action,"WorkingDirectory",Path.GetDirectoryName(path));
            return document.OuterXml;
        }

        static XmlElement Add(XmlDocument document,XmlNode parent,string name,string value) {
            var element=document.CreateElement(name,TaskNamespace);
            if(value!=null) element.InnerText=value;
            parent.AppendChild(element); return element;
        }

        internal static bool IsOwnedTaskXml(string xml,string executablePath,string sid,out bool enabled) {
            enabled=false;
            if(String.IsNullOrWhiteSpace(xml) || xml.Length>131072 || String.IsNullOrWhiteSpace(sid)) return false;
            try {
                var document=new XmlDocument { XmlResolver=null };
                var readerSettings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=131072 };
                using(var input=new StringReader(xml)) using(var reader=XmlReader.Create(input,readerSettings)) document.Load(reader);
                var names=new XmlNamespaceManager(document.NameTable); names.AddNamespace("t",TaskNamespace);
                string root="/t:Task/t:";
                if(Value(document,names,root+"RegistrationInfo/t:Source")!=SourceMarker) return false;
                XmlNodeList principals=document.SelectNodes(root+"Principals/t:Principal",names);
                XmlNodeList actions=document.SelectNodes(root+"Actions/*",names);
                XmlNodeList triggers=document.SelectNodes(root+"Triggers/*",names);
                if(principals.Count!=1 || actions.Count!=1 || actions[0].LocalName!="Exec" || actions[0].NamespaceURI!=TaskNamespace ||
                    triggers.Count!=1 || triggers[0].LocalName!="LogonTrigger" || triggers[0].NamespaceURI!=TaskNamespace) return false;
                XmlNode principal=principals[0];
                string runLevel=Value(principal,names,"t:RunLevel");
                if(!SameUser(Value(principal,names,"t:UserId"),sid) ||
                    Value(principal,names,"t:LogonType")!="InteractiveToken" || (runLevel!=null && runLevel!="LeastPrivilege")) return false;
                XmlNode actionsRoot=document.SelectSingleNode(root+"Actions",names);
                string principalId=principal.Attributes["id"]==null?null:principal.Attributes["id"].Value;
                string context=actionsRoot.Attributes["Context"]==null?null:actionsRoot.Attributes["Context"].Value;
                if(String.IsNullOrEmpty(principalId) || context!=principalId) return false;
                if(!SameUser(Value(triggers[0],names,"t:UserId"),sid)) return false;
                string path=AbsolutePath(executablePath);
                if(!SamePath(Value(actions[0],names,"t:Command"),path) ||
                    !SamePath(Value(actions[0],names,"t:WorkingDirectory"),Path.GetDirectoryName(path)) ||
                    Value(actions[0],names,"t:Arguments")!="--startup") return false;
                enabled=XmlEnabled(document,names,root+"Settings/t:Enabled") && XmlEnabled(triggers[0],names,"t:Enabled");
                return true;
            } catch(XmlException) { return false; }
            catch(ArgumentException) { return false; }
            catch(NotSupportedException) { return false; }
            catch(PathTooLongException) { return false; }
        }

        static string Value(XmlNode node,XmlNamespaceManager names,string xpath) {
            XmlNode found=node.SelectSingleNode(xpath,names); return found==null?null:found.InnerText;
        }
        static bool XmlEnabled(XmlNode node,XmlNamespaceManager names,string xpath) {
            string value=Value(node,names,xpath);
            return value==null || value=="true" || value=="1";
        }
        static bool SameUser(string value,string sid) {
            if(String.IsNullOrWhiteSpace(value)) return false;
            try {
                SecurityIdentifier actual=value.StartsWith("S-1-",StringComparison.OrdinalIgnoreCase)
                    ? new SecurityIdentifier(value)
                    : (SecurityIdentifier)new NTAccount(value).Translate(typeof(SecurityIdentifier));
                return String.Equals(actual.Value,sid,StringComparison.OrdinalIgnoreCase);
            } catch(IdentityNotMappedException) { return false; }
            catch(ArgumentException) { return false; }
        }
        static bool SamePath(string first,string second) {
            if(String.IsNullOrWhiteSpace(first)) return false;
            return String.Equals(AbsolutePath(first).TrimEnd(Path.DirectorySeparatorChar),AbsolutePath(second).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);
        }
        static string AbsolutePath(string value) {
            if(String.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value) || value.IndexOf('"')>=0)
                throw new ArgumentException("An absolute, unquoted executable path is required.");
            return Path.GetFullPath(value);
        }

        static object ComGet(object instance,string property) { return ComInvoke(instance,property,BindingFlags.GetProperty,new object[0]); }
        static object ComCall(object instance,string method,params object[] arguments) { return ComInvoke(instance,method,BindingFlags.InvokeMethod,arguments); }
        static object ComInvoke(object instance,string name,BindingFlags flags,object[] arguments) {
            try { return instance.GetType().InvokeMember(name,flags|BindingFlags.Public|BindingFlags.Instance|BindingFlags.OptionalParamBinding,null,instance,arguments,CultureInfo.InvariantCulture); }
            catch(TargetInvocationException error) {
                if(error.InnerException!=null) throw error.InnerException;
                throw;
            }
        }
        static void Release(object value) {
            if(value==null || !Marshal.IsComObject(value)) return;
            // GetTask/RegisterTask may return the same cached RCW identity.
            // Release only this returned reference, not every alias at once.
            try { Marshal.ReleaseComObject(value); }
            catch(InvalidComObjectException) { }
        }

        internal static bool IsMissingTaskError(Exception error) {
            if(error==null) return false;
            // IDispatch interop maps HRESULTs to framework exceptions on some
            // hosts (notably FileNotFoundException), not always COMException.
            return error.HResult==unchecked((int)0x80070002) || error.HResult==unchecked((int)0x8004130f);
        }

        sealed class TaskConnection : IDisposable {
            object service;
            internal object Folder;
            internal TaskConnection() {
                try {
                    Type type=Type.GetTypeFromProgID("Schedule.Service");
                    if(type==null) throw new StartupRegistrationException("StartupTaskUnavailable","Task Scheduler is unavailable.");
                    service=Activator.CreateInstance(type);
                    ComCall(service,"Connect",Type.Missing,Type.Missing,Type.Missing,Type.Missing);
                    Folder=ComCall(service,"GetFolder","\\");
                } catch(Exception error) {
                    Dispose();
                    if(error is StartupRegistrationException) throw;
                    throw new StartupRegistrationException("StartupTaskUnavailable","Could not connect to Task Scheduler.",error);
                }
            }
            internal object Find(string name) {
                try { return ComCall(Folder,"GetTask",name); }
                catch(Exception error) {
                    if(IsMissingTaskError(error)) return null;
                    throw;
                }
            }
            public void Dispose() { Release(Folder); Folder=null; Release(service); service=null; }
        }
    }
}
