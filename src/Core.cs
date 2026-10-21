// Copyright (C) 2026. GPL-2.0-or-later. See LICENSE.txt.
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using System.Globalization;
using System.Security.Cryptography;

namespace IptvWindows {
public class Channel {
 public string Name="", Url="", Group="", Logo="", TvgId="", Kind="live", Id="", UserAgent="", Referer="";
 public string Key { get { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(Url+"|"+Id))).Replace("-",""); } }
 public override string ToString() { return Name; }
}
public class Profile {
 public string Id=Guid.NewGuid().ToString("N"), Name="РњРѕСЏС‚ РґРѕСЃС‚Р°РІС‡РёРє", Type="m3u", Address="", Username="", Password="", Mac="", Epg="";
 public string Category="live";
 public override string ToString() { return Name; }
}
public class Settings {
 public int BufferMs=2500, Volume=75;
 public bool Hardware=true, AutoPlay=false;
 public string Aspect="РћСЂРёРіРёРЅР°Р»РЅРѕ", Theme="DARK", UserAgent="IPTV-Player-Windows/1.0", LastProfile="", LastChannel="";
 public List<Profile> Profiles=new List<Profile>();
 public List<string> Favorites=new List<string>();
}
public class Storage {
 public readonly string Root;
 public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength=100*1024*1024, RecursionLimit=100 }; }
 public Storage(string root) { Root=root; Directory.CreateDirectory(root); }
 public T Read<T>(string name, T fallback) {
  string p=Path.Combine(Root,name); if(!File.Exists(p)) return fallback;
  try { T value=Json().Deserialize<T>(File.ReadAllText(p,Encoding.UTF8)); if(Object.ReferenceEquals(value,null))throw new InvalidDataException("РџСЂР°Р·РµРЅ С„Р°Р№Р».");return value; }
  catch { throw new InvalidDataException("Р¤Р°Р№Р»СЉС‚ "+name+" Рµ РїРѕРІСЂРµРґРµРЅ. Р—Р°РїР°Р·РµРЅ Рµ Р±РµР· РїСЂРѕРјСЏРЅР°; РІСЉР·СЃС‚Р°РЅРѕРІРµС‚Рµ .bak РєРѕРїРёРµС‚Рѕ РёР»Рё РіРѕ РїСЂРµРјРµСЃС‚РµС‚Рµ."); }
 }
 public void Write(string name, object data) {
  string p=Path.Combine(Root,name), t=p+".tmp"; File.WriteAllText(t,Json().Serialize(data),new UTF8Encoding(false));
  if(File.Exists(p)) File.Replace(t,p,p+".bak"); else File.Move(t,p);
 }
 public List<Channel> Cached(Profile p) { return Read("channels-"+p.Id+".json",new List<Channel>()); }
}
public static class Playlist {
 static string Attr(string line,string key) {
  var m=Regex.Match(line,@"(?:^|\s)"+Regex.Escape(key)+@"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s,]+))",RegexOptions.IgnoreCase);
  return m.Success ? (m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Success?m.Groups[2].Value:m.Groups[3].Value) : "";
 }
 static int Comma(string s) { char q='\0'; for(int i=0;i<s.Length;i++){ if(s[i]=='"' || s[i]=='\''){if(q=='\0')q=s[i];else if(q==s[i])q='\0';} if(s[i]==',' && q=='\0')return i;} return -1; }
 public static List<Channel> Parse(string text,string origin) {
  var list=new List<Channel>(); Channel c=null; string group="";
  foreach(string raw in text.Split('\n')) {
   string line=raw.Trim().TrimStart('\uFEFF'); if(line.Length==0)continue;
   if(line.StartsWith("#EXTINF:",StringComparison.OrdinalIgnoreCase)) { int k=Comma(line); c=new Channel {Name=k<0?"РљР°РЅР°Р»":line.Substring(k+1).Trim(),TvgId=Attr(line,"tvg-id"),Logo=Attr(line,"tvg-logo"),Group=Attr(line,"group-title")}; if(c.Group=="")c.Group=group; }
   else if(line.StartsWith("#EXTGRP:",StringComparison.OrdinalIgnoreCase)){group=line.Substring(8).Trim(); if(c!=null)c.Group=group;}
   else if(line.StartsWith("#EXTVLCOPT:",StringComparison.OrdinalIgnoreCase) && c!=null) { var opt=line.Substring(11); if(opt.StartsWith("http-user-agent="))c.UserAgent=opt.Substring(16); if(opt.StartsWith("http-referrer="))c.Referer=opt.Substring(14); }
   else if(!line.StartsWith("#")) {
    if(c==null)c=new Channel{Name="РљР°РЅР°Р» "+(list.Count+1),Group=group};
    string url=line; int bar=url.IndexOf('|'); if(bar>=0){ foreach(var h in url.Substring(bar+1).Split('&')){int eq=h.IndexOf('='); if(eq<0)continue; string key=h.Substring(0,eq).ToLowerInvariant(), val=Uri.UnescapeDataString(h.Substring(eq+1)); if(key=="user-agent")c.UserAgent=val; if(key=="referer"||key=="referrer")c.Referer=val;} url=url.Substring(0,bar); }
    Uri u=null; bool absolute=Regex.IsMatch(url,@"^[a-zA-Z][a-zA-Z0-9+.-]*:"); if(absolute)Uri.TryCreate(url,UriKind.Absolute,out u); else if(!String.IsNullOrWhiteSpace(origin)) { Uri b; if(Uri.TryCreate(origin,UriKind.Absolute,out b) && (b.Scheme=="http"||b.Scheme=="https"))u=new Uri(b,url); else u=new Uri(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(origin),url))); }
    if(u!=null && new[]{"http","https","rtsp","rtmp","udp","rtp","file"}.Contains(u.Scheme)) {c.Url=u.AbsoluteUri; if(c.Name=="")c.Name="РљР°РЅР°Р» "+(list.Count+1); list.Add(c);} c=null;
   }
  }
  return list;
 }
 public static string EpgFromHeader(string text) { string first=text.Split('\n').FirstOrDefault()??""; string s=Attr(first,"url-tvg"); return s!=""?s:Attr(first,"x-tvg-url"); }
 public static string Export(IEnumerable<Channel> channels) { var b=new StringBuilder("#EXTM3U\r\n"); foreach(var c in channels){b.Append("#EXTINF:-1 tvg-id=\"").Append(c.TvgId.Replace("\"","'")).Append("\" tvg-logo=\"").Append(c.Logo.Replace("\"","'")).Append("\" group-title=\"").Append(c.Group.Replace("\"","'")).Append("\",").Append(c.Name.Replace("\n"," ")).Append("\r\n"); if(c.UserAgent!="")b.Append("#EXTVLCOPT:http-user-agent=").Append(c.UserAgent).Append("\r\n"); if(c.Referer!="")b.Append("#EXTVLCOPT:http-referrer=").Append(c.Referer).Append("\r\n"); b.Append(c.Url).Append("\r\n");} return b.ToString(); }
}
public static class Net {
 public static async Task<byte[]> Bytes(string url,string agent,Dictionary<string,string> headers,int max) {
  Uri uri; if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||!(uri.Scheme=="http"||uri.Scheme=="https"))throw new ArgumentException("РќСѓР¶РµРЅ Рµ РІР°Р»РёРґРµРЅ HTTP РёР»Рё HTTPS Р°РґСЂРµСЃ.");
  var req=(HttpWebRequest)WebRequest.Create(uri); req.UserAgent=agent; req.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate; req.Timeout=25000; req.ReadWriteTimeout=25000;
  if(headers!=null)foreach(var h in headers){if(h.Key=="Referer")req.Referer=h.Value;else req.Headers[h.Key]=h.Value;}
  using(var timeout=new System.Threading.Timer(delegate {req.Abort();},null,30000,System.Threading.Timeout.Infinite))
  try { using(var resp=(HttpWebResponse)await req.GetResponseAsync()) using(var s=resp.GetResponseStream()) using(var b=new MemoryStream()){byte[] buf=new byte[65536];int n;while((n=await s.ReadAsync(buf,0,buf.Length))>0){if(b.Length+n>max)throw new InvalidDataException("РћС‚РіРѕРІРѕСЂСЉС‚ РЅР°РґРІРёС€Р°РІР° РґРѕРїСѓСЃС‚РёРјРёСЏ СЂР°Р·РјРµСЂ.");b.Write(buf,0,n);}return b.ToArray();} }
  catch(WebException e){var r=e.Response as HttpWebResponse; if(r!=null)throw new IOException("РЎСЉСЂРІСЉСЂСЉС‚ РІСЉСЂРЅР° HTTP "+(int)r.StatusCode+". РџСЂРѕРІРµСЂРµС‚Рµ Р°РґСЂРµСЃР° Рё РґР°РЅРЅРёС‚Рµ Р·Р° РґРѕСЃС‚СЉРї."); throw new IOException("РќСЏРјР° РѕС‚РіРѕРІРѕСЂ РѕС‚ РґРѕСЃС‚Р°РІС‡РёРєР°. РџСЂРѕРІРµСЂРµС‚Рµ РІСЂСЉР·РєР°С‚Р° Рё Р°РґСЂРµСЃР°.");}
 }
 public static async Task<string> Text(string url,string agent,Dictionary<string,string> headers) { return Encoding.UTF8.GetString(await Bytes(url,agent,headers,80*1024*1024)).TrimStart('\uFEFF'); }
 public static async Task<object> Json(string url,string agent,Dictionary<string,string> headers){return Storage.Json().DeserializeObject(await Text(url,agent,headers));}
}
public class Provider {
 Settings settings; string dataRoot; public Provider(Settings s,string root=null){settings=s;dataRoot=root;}
 public static string S(object o,string key){var d=o as Dictionary<string,object>;return d!=null&&d.ContainsKey(key)&&d[key]!=null?Convert.ToString(d[key],CultureInfo.InvariantCulture):"";}
 public static object V(object o,string key){var d=o as Dictionary<string,object>;return d!=null&&d.ContainsKey(key)?d[key]:null;}
 static IEnumerable<object> Arr(object o){return o as object[]??new object[0];}
 string Base(Profile p) {return p.Address.Trim().TrimEnd('/');}
 static string Q(string s){return Uri.EscapeDataString(s??"");}
 string Api(Profile p,string action){return Base(p)+"/player_api.php?username="+Q(p.Username)+"&password="+Q(p.Password)+(action==""?"":"&action="+action);}
 public async Task<List<Channel>> Load(Profile p) {
  if(p.Type=="m3u" || p.Type=="file"){
   string origin=p.Address; if(p.Type=="file" && !Path.IsPathRooted(origin) && dataRoot!=null)origin=Path.Combine(dataRoot,origin);
   string text=p.Type=="file"?File.ReadAllText(origin,Encoding.UTF8):await Net.Text(p.Address,settings.UserAgent,null);
   if(String.IsNullOrWhiteSpace(p.Epg))p.Epg=Playlist.EpgFromHeader(text);var ch=Playlist.Parse(text,origin);if(ch.Count==0)throw new InvalidDataException("РќРµ СЃР° РЅР°РјРµСЂРµРЅРё РєР°РЅР°Р»Рё. РћС‡Р°РєРІР° СЃРµ M3U СЃРїРёСЃСЉРє СЃ РІР°Р»РёРґРЅРё Р°РґСЂРµСЃРё.");return ch;
  }
  if(p.Type=="portal")return await Portal(p);
  object account=await Net.Json(Api(p,""),settings.UserAgent,null); object info=V(account,"user_info"); if(S(info,"auth")=="0"||S(info,"status")=="Disabled"||S(info,"status")=="Expired"||info==null)throw new IOException("Р”РѕСЃС‚Р°РІС‡РёРєСЉС‚ РЅРµ РїСЂРёРµ РїСЂРѕС„РёР»Р°. РџСЂРѕРІРµСЂРµС‚Рµ Р°Р±РѕРЅР°РјРµРЅС‚Р° Рё РґР°РЅРЅРёС‚Рµ.");
  string kind=p.Category=="vod"?"vod":p.Category=="series"?"series":"live";
  var cats=new Dictionary<string,string>(); foreach(var c in Arr(await Net.Json(Api(p,"get_"+kind+"_categories"),settings.UserAgent,null)))cats[S(c,"category_id")]=S(c,"category_name");
  var data=await Net.Json(Api(p,kind=="series"?"get_series":"get_"+kind+"_streams"),settings.UserAgent,null); var list=new List<Channel>();
  foreach(var c in Arr(data)){string id=S(c,kind=="series"?"series_id":"stream_id"),cat=S(c,"category_id");string ext=S(c,"container_extension"); if(ext=="")ext=kind=="live"?"ts":"mp4";
   list.Add(new Channel{Name=S(c,"name"),Id=id,TvgId=S(c,"epg_channel_id"),Group=cats.ContainsKey(cat)?cats[cat]:"Р‘РµР· РєР°С‚РµРіРѕСЂРёСЏ",Logo=S(c,"stream_icon"),Kind=kind,Url=kind=="series"?"series:"+id:Base(p)+"/"+(kind=="vod"?"movie":"live")+"/"+Q(p.Username)+"/"+Q(p.Password)+"/"+Q(id)+"."+Q(ext)});
  }
  if(list.Count==0)throw new IOException("Р”РѕСЃС‚Р°РІС‡РёРєСЉС‚ РЅРµ РІСЉСЂРЅР° СЃСЉРґСЉСЂР¶Р°РЅРёРµ РІ С‚Р°Р·Рё РєР°С‚РµРіРѕСЂРёСЏ.");
  if(p.Epg=="")p.Epg=Base(p)+"/xmltv.php?username="+Q(p.Username)+"&password="+Q(p.Password);return list;
 }
 public async Task<List<Channel>> Episodes(Profile p,Channel series){object data=await Net.Json(Api(p,"get_series_info")+"&series_id="+Q(series.Id),settings.UserAgent,null);var seasons=V(data,"episodes") as Dictionary<string,object>; var list=new List<Channel>(); if(seasons!=null)foreach(var season in seasons.OrderBy(x=>x.Key))foreach(var e in Arr(season.Value)){string id=S(e,"id"),ext=S(e,"container_extension");if(ext=="")ext="mp4"; list.Add(new Channel{Name=S(e,"title"),Group="РЎРµР·РѕРЅ "+season.Key,Kind="episode",Id=id,Url=Base(p)+"/series/"+Q(p.Username)+"/"+Q(p.Password)+"/"+id+"."+Q(ext)});}return list;}
 string portalToken="",portalProfileId=""; Dictionary<string,string> portalHeaders; string portalEndpoint="";
 async Task<object> PortalCall(string query){return V(await Net.Json(portalEndpoint+"?"+query+"&JsHttpRequest=1-xml",settings.UserAgent,portalHeaders),"js");}
 async Task<List<Channel>> Portal(Profile p) {
  string b=Base(p);if(b.EndsWith(".php",StringComparison.OrdinalIgnoreCase))portalEndpoint=b;else{if(b.EndsWith("/c"))b=b.Substring(0,b.Length-2);portalEndpoint=b+"/portal.php";}
  portalHeaders=new Dictionary<string,string>{{"Cookie","mac="+Q(p.Mac.ToUpperInvariant())+"; stb_lang=en; timezone=Europe%2FSofia"},{"X-User-Agent","Model: MAG254; Link: Ethernet"}};
  object handshake=await PortalCall("type=stb&action=handshake&token="); portalToken=S(handshake,"token");if(portalToken=="")throw new IOException("РџРѕСЂС‚Р°Р»СЉС‚ РЅРµ РІСЉСЂРЅР° С‚РѕРєРµРЅ. РџСЂРѕРІРµСЂРµС‚Рµ С‚РѕС‡РЅРёСЏ portal.php/server/load.php Р°РґСЂРµСЃ Рё MAC.");portalHeaders["Authorization"]="Bearer "+portalToken;
  await PortalCall("type=stb&action=get_profile"); var cats=new Dictionary<string,string>();foreach(var c in Arr(await PortalCall("type=itv&action=get_genres")))cats[S(c,"id")]=S(c,"title");
  var list=new List<Channel>();var seen=new HashSet<string>();
   foreach(var genreId in cats.Keys.Concat(new[]{"*"}).Distinct()){var genreSeen=new HashSet<string>();for(int page=1;;page++){object data=await PortalCall("type=itv&action=get_ordered_list&genre="+Q(genreId)+"&force_ch_link_check=&fav=0&sortby=number&hd=0&p="+page);var entries=Arr(V(data,"data")).ToArray();if(entries.Length==0)break;int added=0;foreach(var c in entries){string cmd=S(c,"cmd"),id=S(c,"id"),g=S(c,"tv_genre_id");if(seen.Add(id+"|"+cmd)&&genreSeen.Add(id+"|"+cmd)){list.Add(new Channel{Name=S(c,"name"),Url=cmd,Id=id,TvgId=S(c,"xmltv_id"),Group=cats.ContainsKey(g)?cats[g]:"TV",Kind="portal"});added++;}} if(added==0)break; int total; if(Int32.TryParse(S(data,"total_items"),out total)&&list.Count>=total)break;}}
  if(list.Count==0)throw new IOException("РџРѕСЂС‚Р°Р»СЉС‚ РЅРµ РІСЉСЂРЅР° РєР°РЅР°Р»Рё Р·Р° С‚РѕР·Рё MAC Р°РґСЂРµСЃ.");portalProfileId=p.Id;return list;
 }
 public async Task<string> Resolve(Profile p,Channel c){if(c.Kind!="portal")return c.Url;if(p==null)throw new IOException("РР·Р±РµСЂРµС‚Рµ РїСЂРѕС„РёР» Р·Р° С‚РѕР·Рё РїРѕСЂС‚Р°Р».");if(portalHeaders==null||portalProfileId!=p.Id)await Portal(p);object data=await PortalCall("type=itv&action=create_link&cmd="+Q(c.Url)+"&series=&forced_storage=undefined&disable_ad=0&download=0&force_ch_link_check=0");string cmd=S(data,"cmd");if(cmd=="")cmd=c.Url;var m=Regex.Match(cmd,@"(?:https?|rtsp|rtmp|udp)://[^\s""']+");if(!m.Success)throw new IOException("РџРѕСЂС‚Р°Р»СЉС‚ РЅРµ РїСЂРµРґРѕСЃС‚Р°РІРё РІСЉР·РїСЂРѕРёР·РІРѕРґРёРј Р°РґСЂРµСЃ.");return m.Value;}
}
public class Programme { public string Channel="", Title="", Description="";public DateTimeOffset Start,Stop; }
public class Epg {
 public Dictionary<string,List<Programme>> Items=new Dictionary<string,List<Programme>>(StringComparer.OrdinalIgnoreCase);
 public static DateTimeOffset Date(string s){var m=Regex.Match(s??"",@"^(\d{14})(?:\s*([+-]\d{4}))?");if(!m.Success)throw new FormatException("РќРµРІР°Р»РёРґРµРЅ XMLTV С‡Р°СЃ.");DateTime d=DateTime.ParseExact(m.Groups[1].Value,"yyyyMMddHHmmss",CultureInfo.InvariantCulture); string z=m.Groups[2].Value; TimeSpan off=TimeSpan.Zero;if(z!=""){off=new TimeSpan(Int32.Parse(z.Substring(1,2)),Int32.Parse(z.Substring(3,2)),0);if(z[0]=='-')off=-off;}return new DateTimeOffset(d,off);}
 public static Epg Parse(byte[] bytes){if(bytes.Length>1&&bytes[0]==31&&bytes[1]==139){using(var g=new GZipStream(new MemoryStream(bytes),CompressionMode.Decompress))using(var ms=new MemoryStream()){byte[] b=new byte[65536];int n;while((n=g.Read(b,0,b.Length))>0){if(ms.Length+n>150*1024*1024)throw new InvalidDataException("EPG С„Р°Р№Р»СЉС‚ Рµ РїСЂРµРєР°Р»РµРЅРѕ РіРѕР»СЏРј.");ms.Write(b,0,n);}bytes=ms.ToArray();}}
  var epg=new Epg();var set=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=150*1024*1024};
  using(var r=XmlReader.Create(new MemoryStream(bytes),set)){while(r.Read()){if(r.NodeType!=XmlNodeType.Element||r.Name!="programme")continue;var p=new Programme{Channel=r.GetAttribute("channel")??""};try{p.Start=Date(r.GetAttribute("start"));p.Stop=Date(r.GetAttribute("stop"));}catch(FormatException){continue;}using(var sub=r.ReadSubtree()){while(sub.Read()){if(sub.NodeType==XmlNodeType.Element&&(sub.Name=="title"||sub.Name=="desc")){string key=sub.Name;using(var el=sub.ReadSubtree()){el.Read();string value=el.ReadElementContentAsString();if(key=="title"&&p.Title=="")p.Title=value;if(key=="desc"&&p.Description=="")p.Description=value;}}}}
   if(p.Stop<DateTimeOffset.Now.AddDays(-1)||p.Start>DateTimeOffset.Now.AddDays(8))continue;List<Programme> list;if(!epg.Items.TryGetValue(p.Channel,out list)){list=new List<Programme>();epg.Items[p.Channel]=list;}list.Add(p);
  }}foreach(var list in epg.Items.Values)list.Sort((a,b)=>a.Start.CompareTo(b.Start));return epg;
 }
 public Programme Now(string id,DateTimeOffset now){List<Programme> list;return Items.TryGetValue(id,out list)?list.FirstOrDefault(p=>p.Start<=now&&p.Stop>now):null;}
 public Programme Next(string id,DateTimeOffset now){List<Programme> list;return Items.TryGetValue(id,out list)?list.FirstOrDefault(p=>p.Start>now):null;}
}
}
