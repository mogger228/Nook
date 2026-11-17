using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Nook {
 partial class DockWindow {
  async void InitializeFirstRun() {
   if(prefs.InitialSetupComplete || closing) return;
   var imported=new Preferences();
   var completion=new TaskCompletionSource<string>();
   var worker=new Thread(delegate() {
    try { completion.SetResult(TaskbarPinImport.Import(imported)); }
    catch(Exception error) { completion.SetResult("Не удалось импортировать закрепления: "+error.Message); }
   });
   worker.IsBackground=true; worker.SetApartmentState(ApartmentState.STA); worker.Start();
   string status=await completion.Task;
   if(closing) return;
   if(prefs.Pins.Count==0) { prefs.Pins=imported.Pins; prefs.PinDetails=imported.PinDetails; }
   prefs.InitialSetupComplete=true; prefs.PinImportStatus=status;
   Save(); RefreshApps(true); RefreshSearchCatalog(true);
   if(imported.Pins.Count==0) MessageBox.Show(this,status+"\nПрограммы можно закрепить через меню панели.","Nook: первый запуск",MessageBoxButton.OK,MessageBoxImage.Information);
  }
 }
}
