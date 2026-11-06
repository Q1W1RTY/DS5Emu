using System.Runtime.InteropServices;
namespace DS5Link;
internal static class ThemeResources
{
 internal static ControlTheme RequireTheme(this Control control,string name)=>control.FindResource(name) as ControlTheme??throw new InvalidOperationException("Missing theme: "+name);
}
internal static class NativeWindowChrome
{
 internal static void Attach(Window window,Control header){
  header.PointerPressed+=(_,e)=>{
   if(e.GetCurrentPoint(header).Properties.IsLeftButtonPressed && !(e.Source is Visual v && v.GetSelfAndVisualAncestors().OfType<Button>().Any()))window.BeginMoveDrag(e);
  };
 }
}
internal enum MessageBoxButton{OK=0,YesNo=4}
internal enum MessageBoxImage{None=0,Question=32}
internal enum MessageBoxResult{None=0,OK=1,Yes=6,No=7}
internal static class MessageBox
{
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int MessageBoxW(IntPtr owner,string text,string caption,uint flags);
 internal static MessageBoxResult Show(Window owner,string text,string caption,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None,MessageBoxResult defaultResult=MessageBoxResult.No)=>
  (MessageBoxResult)MessageBoxW(owner.TryGetPlatformHandle()?.Handle??IntPtr.Zero,text,caption,(uint)buttons|(uint)image|(buttons==MessageBoxButton.YesNo&&defaultResult==MessageBoxResult.No?0x100u:0));
 internal static MessageBoxResult Show(string text,string caption,MessageBoxButton buttons=MessageBoxButton.OK)=>Show(DesktopLifetime.Current.MainWindow!,text,caption,buttons);
}
internal static class KeyInterop
{
 internal static Key KeyFromVirtualKey(int value){
  if(value>=0x41&&value<=0x5a)return Key.A+(value-0x41);
  if(value>=0x30&&value<=0x39)return Key.D0+(value-0x30);
  if(value>=0x70&&value<=0x7b)return Key.F1+(value-0x70);
  return value switch{0x20=>Key.Space,0x0d=>Key.Enter,0x1b=>Key.Escape,0x09=>Key.Tab,0x21=>Key.PageUp,0x22=>Key.PageDown,0x23=>Key.End,0x24=>Key.Home,0x25=>Key.Left,0x26=>Key.Up,0x27=>Key.Right,0x28=>Key.Down,0x2d=>Key.Insert,0x2e=>Key.Delete,_=>Key.None};
 }
 internal static int VirtualKeyFromKey(Key key){for(int i=1;i<=255;i++)if(KeyFromVirtualKey(i)==key)return i;return 0;}
}
