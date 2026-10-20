using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using IptvWindows;

public static class EpgTests {
 static int passed,failed;
 static readonly List<object> results=new List<object>();
 static readonly DateTimeOffset Start=new DateTimeOffset(DateTime.UtcNow.Date.AddHours(12),TimeSpan.Zero);
 static readonly DateTimeOffset Stop=Start.AddHours(1);
 static byte[] Utf8(string text){return Encoding.UTF8.GetBytes(text);}
 static string Iso(DateTimeOffset value){return value.ToString("yyyy-MM-dd'T'HH:mm:sszzz",CultureInfo.InvariantCulture);}
 static string XmlTime(DateTimeOffset value){return value.ToString("yyyyMMddHHmmss zzz",CultureInfo.InvariantCulture).Replace(":","");}
 static string Xml(string title){return "<?xml version=\"1.0\" encoding=\"utf-8\"?><tv><programme channel=\"synthetic\" start=\""+XmlTime(Start)+"\" stop=\""+XmlTime(Stop)+"\"><title>"+title+"</title><desc>Синтетична програма</desc></programme></tv>";}
 static string Json(object value){return Storage.Json().Serialize(value);}
 static object Record(object starts,object ends){return new {channel="synthetic",start=starts,stop=ends,title="Синтетични новини",description="Собствени локални тестови данни"};}
 static string BasicJson(){return Json(new object[]{Record(Iso(Start),Iso(Stop))});}
 static Epg Read(string text){return EpgFormats.Parse(Utf8(text));}
 static Programme One(Epg value){Check(value.Items.Count==1,"Броят канали не е 1.");Check(value.Items["synthetic"].Count==1,"Броят предавания не е 1.");return value.Items["synthetic"][0];}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void SameTime(Programme p){Check(p.Start==Start&&p.Stop==Stop,"Часовете се различават от UTC контролната стойност.");}
 static void Reject(Action action,string message){try{action();}catch(InvalidDataException ex){Check(ex.Message.IndexOf(message,StringComparison.OrdinalIgnoreCase)>=0,"Неочаквано съобщение: "+ex.Message);return;}throw new Exception("Очакваше се InvalidDataException.");}
 static void Test(string name,Action action){try{action();passed++;results.Add(new{name=name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;results.Add(new{name=name,passed=false,error=ex.GetType().Name+": "+ex.Message});Console.WriteLine("FAIL "+name+": "+ex.Message);}}
 static byte[] Gzip(byte[] data){using(var output=new MemoryStream()){using(var zip=new GZipStream(output,CompressionMode.Compress,true)){zip.Write(data,0,data.Length);}return output.ToArray();}}
 static byte[] Zip(params KeyValuePair<string,string>[] files){using(var output=new MemoryStream()){using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){foreach(var file in files){var entry=zip.CreateEntry(file.Key,CompressionLevel.Optimal);using(var stream=entry.Open()){byte[] data=Utf8(file.Value);stream.Write(data,0,data.Length);}}}return output.ToArray();}}
 static KeyValuePair<string,string> FileEntry(string name,string content){return new KeyValuePair<string,string>(name,content);}
 static decimal Unix(DateTimeOffset time){return (decimal)(time-new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero)).Ticks/TimeSpan.TicksPerSecond;}

 public static int Run() {
  passed=0;failed=0;results.Clear();
  Test("XMLTV UTF-8 and Cyrillic",delegate{Programme p=One(Read(Xml("Новини")));SameTime(p);Check(p.Title=="Новини"&&p.Description=="Синтетична програма","Текстът е загубен.");});
  Test("gzip XMLTV",delegate{SameTime(One(EpgFormats.Parse(Gzip(Utf8(Xml("gzip"))))));});
  Test("XMLTV UTF-16 BOM",delegate{string xml=Xml("UTF-16").Replace("encoding=\"utf-8\"","encoding=\"utf-16\"");byte[] text=Encoding.Unicode.GetBytes(xml),bom=Encoding.Unicode.GetPreamble();var data=new byte[text.Length+bom.Length];Buffer.BlockCopy(bom,0,data,0,bom.Length);Buffer.BlockCopy(text,0,data,bom.Length,text.Length);SameTime(One(EpgFormats.Parse(data)));});
  Test("XMLTV DTD and external entities prohibited",delegate{bool rejected=false;try{Read("<!DOCTYPE tv [<!ENTITY xxe SYSTEM 'file:///C:/synthetic-nonexistent-secret.txt'>]><tv><programme channel='synthetic' start='"+XmlTime(Start)+"' stop='"+XmlTime(Stop)+"'><title>&xxe;</title></programme></tv>");}catch(System.Xml.XmlException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,"XML външни entities са приети.");});
  Test("JSON flat records and ISO offsets",delegate{var p=One(Read(Json(new object[]{Record(Iso(Start.ToOffset(TimeSpan.FromHours(3))),Iso(Stop.ToOffset(TimeSpan.FromHours(3))))})));SameTime(p);Check(p.Title=="Синтетични новини","Заглавието е загубено.");});
  Test("JSON XMLTV timestamps with negative offset",delegate{SameTime(One(Read(Json(new object[]{Record(XmlTime(Start.ToOffset(TimeSpan.FromHours(-4))),XmlTime(Stop.ToOffset(TimeSpan.FromHours(-4))))}))));});
  Test("JSON Unix numeric seconds",delegate{SameTime(One(Read(Json(new object[]{Record(Unix(Start),Unix(Stop))}))));});
  Test("JSON Unix numeric milliseconds",delegate{SameTime(One(Read(Json(new object[]{Record(Unix(Start)*1000,Unix(Stop)*1000)}))));});
  Test("JSON Unix strings",delegate{SameTime(One(Read(Json(new object[]{Record(Unix(Start).ToString(CultureInfo.InvariantCulture),Unix(Stop).ToString(CultureInfo.InvariantCulture))}))));});
  Test("JSON timezone absent means UTC",delegate{SameTime(One(Read(Json(new object[]{Record(Start.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture),Stop.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture))}))));});
  Test("JSON wrappers and field aliases",delegate{SameTime(One(Read(Json(new {epg_listings=new object[]{new {channel_id="synthetic",start_timestamp=Unix(Start),stop_timestamp=Unix(Stop),name="Aliases",desc="Description"}}}))));});
  Test("JSON channel map",delegate{SameTime(One(Read(Json(new Dictionary<string,object>{{"synthetic",new object[]{new {start=Iso(Start),stop=Iso(Stop),title="Map"}}}}))));});
  Test("JSON channels list",delegate{SameTime(One(Read(Json(new {channels=new object[]{new {id="synthetic",programmes=new object[]{new {start=Iso(Start),stop=Iso(Stop),title="Channels"}}}}}))));});
  Test("JSON explicit base64 UTF-8 fields",delegate{var p=One(Read(Json(new object[]{new {channel="synthetic",start=Iso(Start),stop=Iso(Stop),title_base64=Convert.ToBase64String(Utf8("Кодирано")),description_base64=Convert.ToBase64String(Utf8("Описание"))}})));Check(p.Title=="Кодирано"&&p.Description=="Описание","Base64 декодирането е невалидно.");});
  Test("UTF-8 BOM JSON",delegate{SameTime(One(Read("\uFEFF "+BasicJson())));});
  Test("JSON sort and case-insensitive EPG lookup",delegate{var epg=Read(Json(new object[]{Record(Iso(Start.AddHours(2)),Iso(Stop.AddHours(2))),Record(Iso(Start),Iso(Stop))}));Check(epg.Now("SYNTHETIC",Start.AddMinutes(5)).Start==Start,"Now не намира предаването.");Check(epg.Next("synthetic",Start.AddMinutes(5)).Start==Start.AddHours(2),"Next не е подреден по час.");});
  Test("JSON old programme window filtered",delegate{var epg=Read(Json(new object[]{Record(Iso(Start.AddDays(-3)),Iso(Stop.AddDays(-3)))}));Check(epg.Items.Count==0,"Старото предаване не е филтрирано.");});
  Test("JSON invalid interval rejected",delegate{Reject(delegate{Read(Json(new object[]{Record(Iso(Stop),Iso(Start))}));},"stop след start");});
  Test("JSON ambiguous locale date rejected",delegate{Reject(delegate{Read(Json(new object[]{Record("01/02/2026 12:00","01/02/2026 13:00")}));},"невалидни start/stop");});
  Test("JSON invalid timezone minutes rejected",delegate{Reject(delegate{Read(Json(new object[]{Record(Start.ToString("yyyyMMddHHmmss",CultureInfo.InvariantCulture)+" +0360",XmlTime(Stop))}));},"невалидни start/stop");});
  Test("JSON unknown schema explained",delegate{Reject(delegate{Read("{\"foo\":42}");},"схема");});
  Test("TSV header and optional description",delegate{SameTime(One(Read("channel\tstart\tstop\ttitle\tdescription\r\nsynthetic\t"+Iso(Start)+"\t"+Iso(Stop)+"\tНовини\tОписание\r\n")));});
  Test("TSV aliases and quoted tabs/newlines",delegate{var p=One(Read("CHANNEL_ID\tSTART_TIMESTAMP\tEND_TIMESTAMP\tNAME\tDESC\nsynthetic\t"+Unix(Start).ToString(CultureInfo.InvariantCulture)+"\t"+Unix(Stop).ToString(CultureInfo.InvariantCulture)+"\t\"Новини\tи \"\"време\"\"\"\t\"Ред 1\nРед 2\""));SameTime(p);Check(p.Title=="Новини\tи \"време\""&&p.Description=="Ред 1\nРед 2","Quoted TSV съдържанието е променено.");});
  Test("TSV missing header rejected",delegate{Reject(delegate{Read("synthetic\t"+Iso(Start)+"\t"+Iso(Stop)+"\tНовини");},"заглавни колони");});
  Test("TSV mismatched columns rejected",delegate{Reject(delegate{Read("channel\tstart\tstop\ttitle\nsynthetic\t"+Iso(Start)+"\t"+Iso(Stop));},"брой колони");});
  Test("TSV unterminated quote rejected",delegate{Reject(delegate{Read("channel\tstart\tstop\ttitle\nsynthetic\t"+Iso(Start)+"\t"+Iso(Stop)+"\t\"unfinished");},"Незатворена кавичка");});
  Test("TSV column limit enforced",delegate{Reject(delegate{Read(String.Join("\t",Enumerable.Repeat("column",257))+"\n");},"256 колони");Reject(delegate{Read(String.Join("\t",Enumerable.Repeat("column",257)));},"256 колони");});
  Test("TSV field limit enforced",delegate{Reject(delegate{Read("channel\tstart\tstop\ttitle\nsynthetic\t"+Iso(Start)+"\t"+Iso(Stop)+"\t"+new String('x',1024*1024+1));},"1 MiB");});
  Test("ZIP selects first supported entry",delegate{var epg=EpgFormats.Parse(Zip(FileEntry("readme.txt","Synthetic"),FileEntry("guide.json",BasicJson()),FileEntry("later.xml",Xml("Later"))));SameTime(One(epg));Check(One(epg).Title=="Синтетични новини","ZIP не е избрал първия подходящ файл.");});
  Test("ZIP XMLTV",delegate{SameTime(One(EpgFormats.Parse(Zip(FileEntry("guide.xml",Xml("ZIP"))))));});
  Test("ZIP TSV",delegate{SameTime(One(EpgFormats.Parse(Zip(FileEntry("guide.tsv","channel\tstart\tstop\ttitle\nsynthetic\t"+Iso(Start)+"\t"+Iso(Stop)+"\tZIP TSV")))));});
  Test("ZIP reads traversal entry without extracting",delegate{string sentinel=Path.Combine(Path.GetTempPath(),"iptv-synthetic-no-extraction.json");bool existed=File.Exists(sentinel);SameTime(One(EpgFormats.Parse(Zip(FileEntry("../iptv-synthetic-no-extraction.json",BasicJson())))));Check(File.Exists(sentinel)==existed,"ZIP е извлякъл файл във файловата система.");});
  Test("ZIP declared decompressed size limited",delegate{byte[] zip=Zip(FileEntry("guide.json",BasicJson()));for(int i=0;i<zip.Length-28;i++)if(zip[i]==0x50&&zip[i+1]==0x4b&&zip[i+2]==1&&zip[i+3]==2){byte[] length=BitConverter.GetBytes((uint)EpgFormats.MaxBytes+1);Buffer.BlockCopy(length,0,zip,i+24,4);break;}Reject(delegate{EpgFormats.Parse(zip);},"150 MiB");});
  Test("ZIP missing supported file explained",delegate{Reject(delegate{EpgFormats.Parse(Zip(FileEntry("readme.txt","Synthetic")));},"ZIP няма");});
  Test("Invalid UTF-8 JSON/TSV rejected",delegate{Reject(delegate{EpgFormats.Parse(new byte[]{0xff,0x5b,0x5d});},"UTF-8");});
  Test("Empty and unknown inputs explained",delegate{Reject(delegate{EpgFormats.Parse(new byte[0]);},"празен");Reject(delegate{Read("not an EPG");},"Непознат");});
  Console.WriteLine("EPG tests: "+passed+" passed; "+failed+" failed.");return failed;
 }
 public static int Main(string[] args) {
  int errors=Run();
  if(args.Length>0){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));File.WriteAllText(args[0],Json(new {synthetic_only=true,network_requests=0,tested_at_utc=DateTime.UtcNow.ToString("o"),passed=passed,failed=failed,supported_formats=EpgFormats.SupportedFormats,schema_notes=new{channel_aliases=new[]{"channel","channel_id","tvg_id","tvg-id","epg_channel_id"},start_aliases=new[]{"start","start_time","start_timestamp","begin"},stop_aliases=new[]{"stop","end","end_time","end_timestamp","stop_timestamp"},title_aliases=new[]{"title","name"},description_aliases=new[]{"description","desc","plot"},json_explicit_base64=new[]{"title_base64","description_base64"},time="ISO 8601 with Z/offset or UTC default; XMLTV yyyyMMddHHmmss [+-HHmm]; Unix numeric/string seconds, abs >= 100000000000 means milliseconds",time_window="Stop >= now - 1 day and start <= now + 8 days; records must have stop > start",zip="First supported filename in archive order; __MACOSX entries ignored; no extraction; gzip payload must be XMLTV; nested ZIP rejected",tsv="UTF-8 header required; case-insensitive aliases; quoted tabs/newlines and doubled quotes allowed; all data rows must match header column count"},limits=new{max_input_and_inflated_bytes=EpgFormats.MaxBytes,max_json_tsv_programmes=EpgFormats.MaxProgrammes,max_zip_entries=EpgFormats.MaxZipEntries,tsv_max_columns=256,tsv_max_field_characters=1024*1024,json_recursion_limit=64},results=results}),new UTF8Encoding(false));}
  return errors==0?0:1;
 }
}
