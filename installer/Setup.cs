using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Установка DS5Emu")]
[assembly: AssemblyVersion("0.1.3.0")]
internal static class Setup {
 internal static string Work;
 internal static string Log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DS5Emu", "Setup", "last-setup.log");
 [STAThread] static int Main(string[] args) {
  Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
  try {
   Directory.CreateDirectory(Path.GetDirectoryName(Log));
   File.WriteAllText(Log, DateTime.Now.ToString("o") + " DS5Emu Setup\r\n", Encoding.UTF8);
   if (args.Length == 2 && args[0] == "--verify-only") {
    try { Extract(); Verify(); File.WriteAllText(args[1], "{\"ok\":true,\"installed\":false,\"payloadFiles\":" + Directory.GetFiles(Work,"*",SearchOption.AllDirectories).Length + "}"); return 0; }
    catch (Exception e) { Write(e.ToString()); File.WriteAllText(args[1], "{\"ok\":false}"); return 1; }
    finally { Cleanup(); }
   }
   using (var wizard = new Wizard()) { Application.Run(wizard); return wizard.ExitCode; }
  } catch (Exception e) { MessageBox.Show(e.Message, "DS5Emu", MessageBoxButtons.OK,MessageBoxIcon.Error); return 1; }
 }
 internal static void Write(string value) { File.AppendAllText(Log, DateTime.Now.ToString("o") + " " + value + "\r\n", Encoding.UTF8); }
 internal static void Extract() {
  Cleanup();
  Work = Path.Combine(Path.GetTempPath(), "DS5Emu-Setup-" + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(Work);
  using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DS5Emu.Payload"))
  using (var zip = new ZipArchive(stream,ZipArchiveMode.Read)) {
   foreach (var entry in zip.Entries) {
    var path = Path.GetFullPath(Path.Combine(Work,entry.FullName.Replace('/',Path.DirectorySeparatorChar)));
    if (!path.StartsWith(Work + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Недопустимый путь в комплекте.");
    if (entry.Name.Length == 0) { Directory.CreateDirectory(path); continue; }
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    using (var input = entry.Open()) using (var output = new FileStream(path,FileMode.CreateNew)) input.CopyTo(output);
   }
  }
  Write("Payload extracted.");
 }
 internal static void Verify() {
  foreach (var line in File.ReadAllLines(Path.Combine(Work,"files.sha256"))) {
   if (String.IsNullOrWhiteSpace(line)) continue;
   if (line.Length < 67 || line.Substring(64,2) != "  ") throw new IOException("Повреждён список файлов.");
   var path = Path.GetFullPath(Path.Combine(Work,line.Substring(66)));
   if (!path.StartsWith(Work + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Недопустимый путь в списке.");
   using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) {
    var hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
    if (!hash.Equals(line.Substring(0,64),StringComparison.OrdinalIgnoreCase)) throw new IOException("Повреждён файл комплекта: " + Path.GetFileName(path));
   }
  }
  Write("All payload SHA256 hashes verified.");
 }
 internal static void Cleanup() {
  if (Work == null) return;
  var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
  if (Path.GetDirectoryName(Path.GetFullPath(Work)).Equals(parent,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(Work).StartsWith("DS5Emu-Setup-",StringComparison.Ordinal)) {
   try { Directory.Delete(Work,true); } catch (Exception e) { Write("Temporary cleanup: " + e.Message); }
  }
 }
}
internal sealed class Wizard : Form {
 internal int ExitCode = 1602;
 readonly Label title = new Label(); readonly Label text = new Label(); readonly Label status = new Label();
 readonly Button next = new Button(); readonly Button close = new Button(); readonly ProgressBar progress = new ProgressBar();
 readonly CheckBox launch = new CheckBox(); bool busy, finished, reboot;
 internal Wizard() {
  Text="Установка DS5Emu"; ClientSize=new Size(620,420); FormBorderStyle=FormBorderStyle.FixedDialog;
  Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
  MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen; BackColor=Color.FromArgb(28,31,35); ForeColor=Color.FromArgb(235,238,240);
  Font=new Font("Segoe UI",10); AutoScaleMode=AutoScaleMode.Dpi;
  title.SetBounds(30,28,555,50); title.Font=new Font("Segoe UI",23,FontStyle.Bold); title.Text="DS5Emu";
  text.SetBounds(32,100,554,170);
  text.Text="Эмуляция PS5 контроллера по Bluetooth\r\n\r\nМастер установит приложение, службу и недостающие драйверы. .NET, библиотеки и установщики драйверов уже в комплекте.\r\n\r\nWindows 11 x64 · один обычный DualSense\r\nПеред установкой закройте DSX и DS5Emu.";
  progress.SetBounds(32,290,554,8); progress.Visible=false;
  status.SetBounds(32,308,554,36); status.ForeColor=Color.Silver;
  next.SetBounds(386,365,200,32); next.Text="Установить"; next.BackColor=Color.FromArgb(146,221,191); next.ForeColor=Color.Black; next.FlatStyle=FlatStyle.Flat;
  close.SetBounds(250,365,120,32); close.Text="Отмена"; close.FlatStyle=FlatStyle.Flat;
  launch.SetBounds(32,278,554,30); launch.Text="Открыть DS5Emu"; launch.Checked=true; launch.Visible=false;
  Controls.AddRange(new Control[]{title,text,status,progress,next,close,launch});
  next.Click+=async (s,e)=> { if(finished){if(launch.Checked && !reboot)OpenApp(); Close();}else await Install(); };
  close.Click+=(s,e)=>Close(); FormClosing+=(s,e)=>{if(busy)e.Cancel=true;else Setup.Cleanup();};
 }
 async Task Install() {
  busy=true; next.Enabled=false; close.Enabled=false; progress.Visible=true; progress.Style=ProgressBarStyle.Marquee;
  try {
   status.Text="Распаковка и проверка комплекта…";
   int code=await Task.Run(()=>{
    Setup.Extract(); Setup.Verify();
    BeginInvoke((Action)(()=>status.Text="Установка компонентов. Подтвердите запрос Windows…"));
    var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),
      "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(Setup.Work,"Install.ps1") + "\" -InstallerMode");
    info.UseShellExecute=false; info.CreateNoWindow=true; info.RedirectStandardOutput=true; info.RedirectStandardError=true;
    using(var process=Process.Start(info)) {
     var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
     process.WaitForExit(); Task.WaitAll(stdout,stderr); Setup.Write(stdout.Result); Setup.Write(stderr.Result); return process.ExitCode;
    }
   });
   if(code!=0 && code!=3010)throw new IOException("Установка не завершена (код " + code + ").\r\nЖурнал: " + Setup.Log + "\r\nПодробности: %ProgramData%\\DSEmu\\setup.log");
   reboot=code==3010; finished=true; ExitCode=code; title.Text="Готово";
   text.Text=reboot ? "DS5Emu установлен.\r\n\r\nДрайверам нужна перезагрузка Windows. Сохраните работу и перезагрузите компьютер, затем откройте DS5Emu из меню «Пуск»." : "DS5Emu установлен.\r\n\r\n1. Сопрягите DualSense с Windows по Bluetooth.\r\n    Удерживайте Create + PS до мигания индикатора,\r\n    затем добавьте Bluetooth-устройство в Windows.\r\n2. Откройте DS5Emu и включите эмуляцию PS5.\r\n3. Для нативных эффектов отключите Steam Input\r\n    в настройках нужной игры, если он мешает.";
   status.Text=reboot ? "Автоматическая перезагрузка не выполняется." : "Приложение доступно в меню «Пуск».";
   launch.Visible=!reboot; next.Text="Завершить"; close.Visible=false;
  }catch(Exception e){ExitCode=1;Setup.Write(e.ToString()); status.Text="Установка не завершена. Можно повторить."; MessageBox.Show(this,e.Message,"DS5Emu",MessageBoxButtons.OK,MessageBoxIcon.Error);}
  finally{busy=false; next.Enabled=true; close.Enabled=true; progress.Visible=false;}
 }
 void OpenApp() {
  try{Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DSEmu","DS5Emu.exe"),"--show"){UseShellExecute=true});}
  catch(Exception e){MessageBox.Show(this,e.Message,"DS5Emu");}
 }
}
