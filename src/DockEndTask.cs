using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Nook {
 partial class DockWindow {
  bool endingTask;
  ContextMenu CreateDockMenu() {
   var menu=DockMenuTheme.Create(prefs);
   menu.Opened+=delegate { ReturnHoverWave(); HoldDockMenu(true); };
   menu.Closed+=delegate { HoldDockMenu(false); };
   return menu;
  }
  void AddEndTask(ContextMenu menu,AppItem app) {
   var command=new MenuItem { Header="Завершить задачу",Tag="danger",IsEnabled=false };
   List<TaskIdentity> roots=null;
   menu.Items.Add(new Separator()); menu.Items.Add(command);
   menu.Opened+=delegate {
    string explanation; roots=TaskTerminator.Capture(app,out explanation);
    command.IsEnabled=!endingTask && roots.Count>0;
    command.ToolTip=roots.Count>0 ? "Принудительно завершить приложение и связанные процессы. Несохранённые изменения будут потеряны." : explanation;
    ToolTipService.SetShowOnDisabled(command,true);
   };
   command.Click+=delegate {
    if(endingTask || roots==null || roots.Count==0) return;
    endingTask=true; command.IsEnabled=false;
    var selected=roots;
    Task.Run(delegate { return TaskTerminator.EndWithElevation(selected); }).ContinueWith(delegate(Task<TaskEndResult> work) {
     var result=work.Status==TaskStatus.RanToCompletion ? work.Result : new TaskEndResult { Error="Не удалось завершить приложение." };
     if(work.IsFaulted && work.Exception!=null) result.Error=work.Exception.GetBaseException().Message;
     if(Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
     try { Dispatcher.BeginInvoke(new Action(delegate {
      endingTask=false; if(closing) return;
      RefreshApps(true);
      if(!result.Success && !result.Cancelled) MessageBox.Show(result.Error,"Завершить задачу",MessageBoxButton.OK,MessageBoxImage.Information);
     })); } catch(InvalidOperationException) { }
    });
   };
  }
 }
}
