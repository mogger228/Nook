using System;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Nook {
 // One immutable-by-convention snapshot, built on the calling thread. Read on a worker:
 // network adapter enumeration and the Bluetooth driver can take time on some systems.
 // No polling, events, device changes, network requests or retained native handles here.
 internal sealed class StatusSnapshot {
  public string BatteryText { get; internal set; }
  public string BatteryHint { get; internal set; }
  public string WifiText { get; internal set; }
  public string WifiHint { get; internal set; }
  public string BluetoothText { get; internal set; }
  public string BluetoothHint { get; internal set; }
  public string DateText { get; internal set; }
  public string DateHint { get; internal set; }
  public bool BatteryAvailable { get; internal set; }
  public bool WifiConnected { get; internal set; }
  public bool BluetoothAvailable { get; internal set; }
 }

 internal static class DesktopStatus {
  public static StatusSnapshot ReadDock() {
   var culture=CultureInfo.CurrentCulture; var now=DateTime.Now;
   var status=new StatusSnapshot {
    BatteryText="—",BatteryHint="Сведения о батарее недоступны.",
    WifiText="—",WifiHint="Сведения о Wi-Fi недоступны.",
    BluetoothText="—",BluetoothHint="Сведения об адаптере Bluetooth недоступны.",
    DateText=now.ToString("dd.MM.yyyy",culture),DateHint=now.ToString("D",culture)
   };
   ReadBattery(status); ReadWifi(status); return status;
  }
  public static StatusSnapshot Read() {
   var culture=CultureInfo.CurrentCulture;
   var now=DateTime.Now;
   var status=new StatusSnapshot {
    BatteryText="—",BatteryHint="Сведения о батарее недоступны.",
    WifiText="—",WifiHint="Сведения о Wi-Fi недоступны.",
    BluetoothText="—",BluetoothHint="Сведения об адаптере Bluetooth недоступны.",
    DateText=now.ToString("dd.MM.yyyy",culture),DateHint=now.ToString("D",culture)
   };
   ReadBattery(status);
   ReadWifi(status);
   ReadBluetooth(status);
   return status;
  }

  static void ReadBattery(StatusSnapshot status) {
   try {
    SYSTEM_POWER_STATUS power;
    if(!GetSystemPowerStatus(out power)) return;
    if(power.BatteryFlag!=255 && (power.BatteryFlag&128)!=0) {
     status.BatteryHint="Батарея не обнаружена. На стационарном компьютере это нормально.";
     return;
    }
    bool knownPercentage=power.BatteryLifePercent<=100;
    status.BatteryAvailable=power.BatteryFlag!=255 || knownPercentage;
    if(!status.BatteryAvailable) return;
    status.BatteryText=knownPercentage ? power.BatteryLifePercent.ToString(CultureInfo.CurrentCulture)+"%" : "—";
    string charge=knownPercentage ? "Заряд батареи: "+status.BatteryText+"." : "Уровень заряда батареи недоступен.";
    bool charging=power.BatteryFlag!=255 && (power.BatteryFlag&8)!=0;
    string supply=charging ? " Заряжается." : power.ACLineStatus==1 ? " Питание от сети." : power.ACLineStatus==0 ? " Питание от батареи." : " Источник питания неизвестен.";
    status.BatteryHint=charge+supply;
   } catch(ExternalException) { } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
  }

  static void ReadWifi(StatusSnapshot status) {
   try {
    var adapters=NetworkInterface.GetAllNetworkInterfaces().Where(adapter=>adapter.NetworkInterfaceType==NetworkInterfaceType.Wireless80211).ToArray();
    var connected=adapters.FirstOrDefault(adapter=>adapter.OperationalStatus==OperationalStatus.Up);
    status.WifiConnected=connected!=null;
    status.WifiText=connected!=null ? "Подключён" : "Нет связи";
    status.WifiHint=connected!=null ? "Беспроводной адаптер активен: "+connected.Name+". Доступ в интернет не проверялся." : adapters.Length==0 ? "Беспроводной адаптер Wi-Fi не обнаружен." : "Активное подключение Wi-Fi не обнаружено. Интернет может работать через Ethernet или другой интерфейс.";
   } catch(NetworkInformationException) { } catch(InvalidOperationException) { } catch(System.Security.SecurityException) { }
  }

  static void ReadBluetooth(StatusSnapshot status) {
   IntPtr search=IntPtr.Zero,radio=IntPtr.Zero;
   try {
    var parameters=new BLUETOOTH_FIND_RADIO_PARAMS { Size=(uint)Marshal.SizeOf(typeof(BLUETOOTH_FIND_RADIO_PARAMS)) };
    search=BluetoothFindFirstRadio(ref parameters,out radio);
    if(search==IntPtr.Zero || radio==IntPtr.Zero) {
     int error=Marshal.GetLastWin32Error();
     if(error==259 || error==1168) { status.BluetoothText="Не найден"; status.BluetoothHint="Доступный адаптер Bluetooth не обнаружен. Адаптер может отсутствовать, быть отключён или недоступен системе."; }
     return;
    }
    status.BluetoothAvailable=true;
    status.BluetoothText="Доступен";
    status.BluetoothHint="Система обнаружила адаптер Bluetooth. Это не подтверждает, что Bluetooth включён или устройство подключено.";
   } catch(ExternalException) { } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { } catch(System.Security.SecurityException) { }
   finally {
    if(radio!=IntPtr.Zero) CloseHandle(radio);
    if(search!=IntPtr.Zero) BluetoothFindRadioClose(search);
   }
  }

  [StructLayout(LayoutKind.Sequential)] struct SYSTEM_POWER_STATUS {
   public byte ACLineStatus,BatteryFlag,BatteryLifePercent,SystemStatusFlag;
   public uint BatteryLifeTime,BatteryFullLifeTime;
  }
  [StructLayout(LayoutKind.Sequential)] struct BLUETOOTH_FIND_RADIO_PARAMS { public uint Size; }
  [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
  [DllImport("bthprops.cpl",SetLastError=true)] static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS parameters,out IntPtr radio);
  [DllImport("bthprops.cpl",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool BluetoothFindRadioClose(IntPtr search);
  [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool CloseHandle(IntPtr handle);
 }
}
