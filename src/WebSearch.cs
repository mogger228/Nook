using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Nook {
 public sealed class WebSearchOptions {
  public string Engine="Google", Browser="", ChromeProfile="", Color="#FFFFFF";
  public double Opacity=.88, Radius=24, Width=520, FontSize=19, X=.5, Y=.35;
  public bool Suggestions=true;
 }

 static class WebSearch {
  internal static Uri Address(string text) {
   if(text.Length==0 || text.Any(c=>char.IsWhiteSpace(c) || char.IsControl(c)) || text.Contains("\\")) return null;
   bool explicitScheme=text.StartsWith("https://",StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://",StringComparison.OrdinalIgnoreCase);
   if(!explicitScheme && (text.Contains("://") || text.StartsWith("/") || text.Contains("@"))) return null;
   Uri uri;
   if(!Uri.TryCreate(explicitScheme ? text : "https://"+text,UriKind.Absolute,out uri) || uri.UserInfo.Length!=0 || uri.HostNameType==UriHostNameType.Unknown || uri.HostNameType==UriHostNameType.Basic) return null;
   if(!explicitScheme && uri.HostNameType==UriHostNameType.Dns && uri.Host.IndexOf('.')<0 && !uri.IsLoopback) return null;
   return uri;
  }

  static bool IsYandex(string engine) { return engine=="Яндекс"; }
  internal static string Resolve(string text,string engine) {
   var address=Address(text);
   return address!=null ? address.AbsoluteUri : QueryUrl(text,engine);
  }
  static string QueryUrl(string text,string engine) {
   return (IsYandex(engine) ? "https://yandex.ru/search/?text=" : "https://www.google.com/search?q=")+Uri.EscapeDataString(text);
  }

  internal static async Task<List<SearchApp>> Suggest(string query,string engine,CancellationToken token) {
   string prefix=IsYandex(engine) ? "https://suggest.yandex.ru/suggest-ya.cgi?v=4&part=" : "https://www.google.ru/complete/search?client=firefox&q=";
   var request=(HttpWebRequest)WebRequest.Create(prefix+Uri.EscapeDataString(query));
   request.AllowAutoRedirect=false;
   using(token.Register(request.Abort))
   using(var response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false)) {
    if(response.StatusCode!=HttpStatusCode.OK) return new List<SearchApp>();
    using(var stream=response.GetResponseStream())
    using(var buffer=new MemoryStream()) {
     byte[] chunk=new byte[4096]; int count;
     while((count=await stream.ReadAsync(chunk,0,chunk.Length,token).ConfigureAwait(false))!=0) {
      if(buffer.Length+count>32768) return new List<SearchApp>();
      buffer.Write(chunk,0,count);
     }
     return ParseSuggestions(System.Text.Encoding.UTF8.GetString(buffer.ToArray()),engine);
    }
   }
  }

  internal static List<SearchApp> ParseSuggestions(string json,string engine) {
   var root=new JavaScriptSerializer { MaxJsonLength=32768,RecursionLimit=16 }.DeserializeObject(json) as object[];
   var rows=root!=null && root.Length>1 ? root[1] as object[] : null;
   return (rows ?? new object[0]).OfType<string>().Select(s=>s.Trim())
    .Where(s=>s.Length>0 && s.Length<=256 && !s.Any(char.IsControl))
    .Distinct(StringComparer.OrdinalIgnoreCase).Take(6)
    .Select(s=>new SearchApp { Name=s,Target=QueryUrl(s,engine) }).ToList();
  }

  internal static string ChromePath() {
   const string appPath=@"Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";
   foreach(var hive in new[] { Registry.CurrentUser,Registry.LocalMachine }) {
    using(var key=hive.OpenSubKey(appPath)) {
     var path=key==null ? null : key.GetValue(null) as string;
     if(File.Exists(path)) return path;
    }
   }
   foreach(var folder in new[] { Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86,Environment.SpecialFolder.LocalApplicationData }) {
    string directory=Environment.GetFolderPath(folder);
    if(directory.Length==0) continue;
    string path=Path.Combine(directory,@"Google\Chrome\Application\chrome.exe");
    if(File.Exists(path)) return path;
   }
   return null;
  }
 }
}
