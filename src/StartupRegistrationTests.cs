using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;

namespace Polka {
    // Pure XML/command tests: no current-user registry, tasks, processes or UI.
    internal static class StartupRegistrationTests {
        static int checks;
        static readonly string Executable=Path.Combine(Path.GetTempPath(),"Yarnelo & test","Ярлыки","Yarnelo.exe");
        const string Sid="S-1-5-21-111-222-333-1001";
        static void Check(bool result,string message) { if(!result) throw new Exception(message); checks++; }
        static XmlDocument Document(string xml) { var result=new XmlDocument { XmlResolver=null }; result.LoadXml(xml); return result; }
        static XmlNamespaceManager Names(XmlDocument document) { var result=new XmlNamespaceManager(document.NameTable); result.AddNamespace("t",StartupRegistration.TaskNamespace); return result; }
        static string Edit(string source,string xpath,string value) {
            var document=Document(source); document.SelectSingleNode(xpath,Names(document)).InnerText=value; return document.OuterXml;
        }
        static void Reject(string xml,string reason) { bool enabled; Check(!StartupRegistration.IsOwnedTaskXml(xml,Executable,Sid,out enabled),reason); }
        public static int Main() {
            try {
                string xml=StartupRegistration.CreateTaskXml(Executable,Sid,true);
                bool enabled;
                Check(StartupRegistration.IsOwnedTaskXml(xml,Executable,Sid,out enabled) && enabled,"a generated task is owned and enabled");
                var document=Document(xml); var names=Names(document);
                Check(document.SelectSingleNode("/t:Task/t:Triggers/t:LogonTrigger/t:Delay",names).InnerText=="PT10S","logon delay is ten seconds");
                Check(document.SelectSingleNode("/t:Task/t:Settings/t:MultipleInstancesPolicy",names).InnerText=="IgnoreNew","duplicate task starts do not create parallel processes");
                Check(document.SelectSingleNode("/t:Task/t:Settings/t:ExecutionTimeLimit",names).InnerText=="PT0S","the running panel has no scheduler time limit");
                Check(document.SelectSingleNode("/t:Task/t:Settings/t:DisallowStartIfOnBatteries",names).InnerText=="false","battery power permits startup");
                Check(document.SelectSingleNode("/t:Task/t:Settings/t:StopIfGoingOnBatteries",names).InnerText=="false","battery transition does not terminate panel");
                Check(document.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:RunLevel",names).InnerText=="LeastPrivilege","no administrator run level");
                Check(document.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:LogonType",names).InnerText=="InteractiveToken","task runs only in signed-in user session");
                Check(document.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Command",names).InnerText==Executable,"spaces, ampersands and Unicode paths survive XML encoding");
                Check(document.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:WorkingDirectory",names).InnerText==Path.GetDirectoryName(Executable),"working directory follows binary");
                Check(document.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Arguments",names).InnerText=="--startup","startup has an explicit diagnostic source flag");
                Check(StartupRegistration.IsOwnedTaskXml(StartupRegistration.CreateTaskXml(Executable,Sid,false),Executable,Sid,out enabled) && !enabled,"a disabled owned task stays disabled");
                Check(StartupRegistration.IsOwnedTaskXml(Edit(xml,"/t:Task/t:Triggers/t:LogonTrigger/t:Enabled","false"),Executable,Sid,out enabled) && !enabled,"disabled logon trigger counts as disabled");
                var defaults=Document(xml);
                var defaultRunLevel=defaults.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:RunLevel",Names(defaults));
                defaultRunLevel.ParentNode.RemoveChild(defaultRunLevel);
                Check(StartupRegistration.IsOwnedTaskXml(defaults.OuterXml,Executable,Sid,out enabled) && enabled,"Windows may omit the default LeastPrivilege run level");
                using(var current=WindowsIdentity.GetCurrent()) {
                    string currentSid=current.User.Value;
                    string normalized=StartupRegistration.CreateTaskXml(Executable,currentSid,true);
                    normalized=Edit(normalized,"/t:Task/t:Triggers/t:LogonTrigger/t:UserId",current.Name);
                    Check(StartupRegistration.IsOwnedTaskXml(normalized,Executable,currentSid,out enabled) && enabled,"Windows-normalized account trigger resolves to the same signed-in SID");
                    normalized=Edit(normalized,"/t:Task/t:Principals/t:Principal/t:UserId",current.Name);
                    Check(StartupRegistration.IsOwnedTaskXml(normalized,Executable,currentSid,out enabled) && enabled,"Windows-normalized principal account resolves to the same signed-in SID");
                    Check(!StartupRegistration.IsOwnedTaskXml(normalized,Executable,Sid,out enabled),"a mapped account cannot claim a different SID");
                }
                Reject(Edit(xml,"/t:Task/t:RegistrationInfo/t:Source","another-app"),"foreign source marker is not owned");
                Reject(Edit(xml,"/t:Task/t:Principals/t:Principal/t:UserId","S-1-5-18"),"SYSTEM task is not owned");
                Reject(Edit(xml,"/t:Task/t:Principals/t:Principal/t:LogonType","ServiceAccount"),"service account is not owned");
                Reject(Edit(xml,"/t:Task/t:Principals/t:Principal/t:RunLevel","HighestAvailable"),"elevated task is not owned");
                Reject(Edit(xml,"/t:Task/t:Triggers/t:LogonTrigger/t:UserId","S-1-5-21-9"),"another user's trigger is not owned");
                Reject(Edit(xml,"/t:Task/t:Actions/t:Exec/t:Command",Path.Combine(Path.GetTempPath(),"Other","Yarnelo.exe")),"another installation's task is preserved");
                Reject(Edit(xml,"/t:Task/t:Actions/t:Exec/t:WorkingDirectory",Path.GetTempPath()),"foreign working directory is rejected");
                Reject(Edit(xml,"/t:Task/t:Actions/t:Exec/t:Arguments","--startup --unknown"),"extra execution arguments are rejected");
                var extraAction=Document(xml); var actions=extraAction.SelectSingleNode("/t:Task/t:Actions",Names(extraAction)); actions.AppendChild(actions.FirstChild.CloneNode(true));
                Reject(extraAction.OuterXml,"a second execution action prevents ownership");
                var extraTrigger=Document(xml); var triggers=extraTrigger.SelectSingleNode("/t:Task/t:Triggers",Names(extraTrigger)); triggers.AppendChild(triggers.FirstChild.CloneNode(true));
                Reject(extraTrigger.OuterXml,"a second trigger prevents ownership");
                Reject("<Task>","malformed XML is rejected");
                Reject("<!DOCTYPE Task [<!ENTITY secret SYSTEM 'file:///nonexistent'>]><Task>&secret;</Task>","external entity documents are rejected without loading files");
                Reject(new string('x',131073),"oversized XML is rejected");
                Reject(xml.Replace("Context=\"Author\"","Context=\"Other\""),"foreign action principal is rejected");
                Check(StartupRegistration.IsOwnedTaskXml(xml,Executable.ToUpperInvariant(),Sid,out enabled),"Windows path ownership is case insensitive");
                foreach(string command in new[]{Executable,"\""+Executable+"\""," \""+Executable+"\" ","\""+Executable+"\" --startup"})
                    Check(StartupRegistration.IsOwnedRunCommand(command,Executable),"known direct Run command belongs to binary");
                foreach(string command in new[]{"cmd.exe /c \""+Executable+"\"","\""+Executable+"\" --unknown",Path.Combine(Path.GetTempPath(),"Other","Yarnelo.exe"),"%APPDATA%\\Yarnelo.exe",String.Empty})
                    Check(!StartupRegistration.IsOwnedRunCommand(command,Executable),"unrelated/ambiguous Run commands are preserved");
                Check(StartupRegistration.ApprovalAllowsStartup(null),"missing approval record retains Windows default enabled behavior");
                foreach(uint value in new uint[]{2,6}) Check(StartupRegistration.ApprovalAllowsStartup(BitConverter.GetBytes(value)),"known enabled states are allowed");
                foreach(uint value in new uint[]{0,1,3,4,5,7,8,UInt32.MaxValue}) Check(!StartupRegistration.ApprovalAllowsStartup(BitConverter.GetBytes(value)),"disabled and unknown approval states cannot auto-enable startup");
                Check(!StartupRegistration.ApprovalAllowsStartup(new byte[2]) && !StartupRegistration.ApprovalAllowsStartup("enabled"),"malformed approval records are conservative");
                bool rejected=false;
                try { StartupRegistration.CreateTaskXml("Yarnelo.exe",Sid,true); } catch(ArgumentException) { rejected=true; }
                Check(rejected,"relative binary paths are rejected");
                Check(StartupRegistration.IsMissingTaskError(new FileNotFoundException()),"reflection-mapped missing-file HRESULT means an absent task");
                Check(StartupRegistration.IsMissingTaskError(new COMException("missing",unchecked((int)0x80070002))),"COM missing-file HRESULT means an absent task");
                Check(StartupRegistration.IsMissingTaskError(new COMException("missing",unchecked((int)0x8004130f))),"scheduler missing-task HRESULT means an absent task");
                Check(!StartupRegistration.IsMissingTaskError(new UnauthorizedAccessException()),"access denied must not be treated as an absent task");
                Check(!StartupRegistration.IsMissingTaskError(new COMException("unavailable",unchecked((int)0x800706ba))),"an unavailable service must not be treated as an absent task");
                Check(!StartupRegistration.IsMissingTaskError(new IOException()),"other IO failures remain errors");
                Check(!StartupRegistration.IsMissingTaskError(null),"a missing exception is not a missing task result");
                Console.WriteLine("PASS: "+checks+" startup ownership and XML checks; registry and tasks untouched."); return 0;
            } catch(Exception error) { Console.WriteLine(error); return 1; }
        }
    }
}
