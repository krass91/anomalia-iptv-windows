// Copyright (C) 2026. GPL-2.0-or-later. See LICENSE.txt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace IptvWindows {
// This adapter deliberately declares its JSON/TSV schemas. There is no universal
// JSON or TSV EPG standard, so unknown shapes fail instead of guessing columns.
public static class EpgFormats {
 public const int MaxBytes=150*1024*1024, MaxProgrammes=500000, MaxZipEntries=10000;
 public const string SupportedFormats="XMLTV; gzip(XMLTV); ZIP с първи .xml/.xmltv/.json/.tsv/.gz файл. JSON: масив записи, programmes/programs/epg_listings/events, channelId -> масив или channels:[{id,programmes:[...]}]. TSV: заглавен ред channel, start, stop, title и незадължително description. Часове: ISO 8601, XMLTV или Unix seconds/milliseconds; без зона = UTC. Лимити: 150 MiB вход/разархивиран файл, 500 000 JSON/TSV предавания, 10 000 ZIP записа; без вложен ZIP.";
 static readonly string[] ChannelKeys={"channel","channel_id","tvg_id","tvg-id","epg_channel_id"};
 static readonly string[] StartKeys={"start","start_time","start_timestamp","begin"};
 static readonly string[] StopKeys={"stop","end","end_time","end_timestamp","stop_timestamp"};
 static readonly string[] TitleKeys={"title","name"}, DescriptionKeys={"description","desc","plot"};
 static readonly string[] ProgrammeKeys={"programmes","programs","epg_listings","events"};
 static readonly DateTimeOffset Epoch=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero);

 public static Epg Parse(byte[] bytes) { return ParseData(bytes,true); }
 static Epg ParseData(byte[] bytes,bool allowZip) {
  if(bytes==null || bytes.Length==0)throw new InvalidDataException("EPG файлът е празен. "+SupportedFormats);
  if(bytes.Length>MaxBytes)throw new InvalidDataException("EPG файлът надвишава лимита от 150 MiB.");
  if(bytes.Length>=2 && bytes[0]==0x1f && bytes[1]==0x8b)return Epg.Parse(bytes);
  if(bytes.Length>=4 && bytes[0]==0x50 && bytes[1]==0x4b && ((bytes[2]==3&&bytes[3]==4)||(bytes[2]==5&&bytes[3]==6)||(bytes[2]==7&&bytes[3]==8))) {
   if(!allowZip)throw new InvalidDataException("Вложени ZIP архиви за EPG не се поддържат.");
   return ParseZip(bytes);
  }
  // XML decoding is delegated to XmlReader, which also honours UTF-16 XML BOMs.
  int at=0; if(bytes.Length>=3&&bytes[0]==0xef&&bytes[1]==0xbb&&bytes[2]==0xbf)at=3;
  while(at<bytes.Length&&(bytes[at]==32||bytes[at]==9||bytes[at]==10||bytes[at]==13))at++;
  if((at<bytes.Length && bytes[at]=='<') || (bytes.Length>=2&&((bytes[0]==0xff&&bytes[1]==0xfe)||(bytes[0]==0xfe&&bytes[1]==0xff))))return Epg.Parse(bytes);
  string data;
  try { data=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF'); }
  catch(DecoderFallbackException) {throw new InvalidDataException("JSON/TSV EPG трябва да бъде записан като UTF-8. "+SupportedFormats);}
  string trimmed=data.TrimStart();
  if(trimmed.StartsWith("[")||trimmed.StartsWith("{"))return ParseJson(data);
  if(data.IndexOf('\t')>=0)return ParseTsv(data);
  throw new InvalidDataException("Непознат EPG формат. "+SupportedFormats);
 }

 static Epg ParseZip(byte[] bytes) {
  try {
   using(var archive=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read,false)) {
    if(archive.Entries.Count>MaxZipEntries)throw new InvalidDataException("EPG ZIP надвишава лимита от 10 000 записа.");
    foreach(var entry in archive.Entries) {
     if(entry.Name.Length==0 || entry.FullName.StartsWith("__MACOSX/",StringComparison.OrdinalIgnoreCase))continue;
     string extension=Path.GetExtension(entry.Name).ToLowerInvariant();
     if(extension!=".xml"&&extension!=".xmltv"&&extension!=".json"&&extension!=".tsv"&&extension!=".gz"&&extension!=".gzip")continue;
     if(entry.Length>MaxBytes)throw new InvalidDataException("Разархивираният EPG файл надвишава лимита от 150 MiB.");
     using(var input=entry.Open())using(var output=new MemoryStream()) {
      var buffer=new byte[65536]; int count;
      while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>MaxBytes)throw new InvalidDataException("Разархивираният EPG файл надвишава лимита от 150 MiB.");output.Write(buffer,0,count);}
      return ParseData(output.ToArray(),false);
     }
    }
   }
  } catch(InvalidDataException){throw;} catch(IOException ex){throw new InvalidDataException("EPG ZIP архивът не може да бъде прочетен.",ex);}
  throw new InvalidDataException("EPG ZIP няма .xml/.xmltv/.json/.tsv/.gz файл. "+SupportedFormats);
 }

 static object Field(Dictionary<string,object> record,params string[] names) {
  foreach(string name in names){object value;if(record.TryGetValue(name,out value))return value;foreach(var pair in record)if(String.Equals(pair.Key,name,StringComparison.OrdinalIgnoreCase))return pair.Value;}
  return null;
 }
 static string Text(object value){return value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture);}
 static bool IsArray(object value){return value is object[] || value is System.Collections.ArrayList;}
 static IEnumerable<object> Array(object value) {var array=value as object[];if(array!=null)return array;var list=value as System.Collections.ArrayList;if(list!=null)return list.Cast<object>();throw new InvalidDataException("EPG JSON очаква масив от предавания. "+SupportedFormats);}
 static Epg ParseJson(string data) {
  object root;
  try {root=new JavaScriptSerializer{MaxJsonLength=MaxBytes,RecursionLimit=64}.DeserializeObject(data);}
  catch(ArgumentException ex){throw new InvalidDataException("Невалиден EPG JSON. "+SupportedFormats,ex);}
  catch(InvalidOperationException ex){throw new InvalidDataException("EPG JSON е твърде дълбок или невалиден.",ex);}
  var result=new Epg();int count=0;
  JsonContainer(root,"",result,ref count,0);
  Sort(result);return result;
 }
 static void JsonContainer(object data,string inherited,Epg epg,ref int count,int depth) {
  if(depth>8)throw new InvalidDataException("Неподдържано вложено EPG JSON съдържание.");
  if(IsArray(data)){foreach(object item in Array(data))JsonRecord(item,inherited,epg,ref count);return;}
  var dictionary=data as Dictionary<string,object>;
  if(dictionary==null)throw new InvalidDataException("Неподдържана EPG JSON схема. "+SupportedFormats);
  object programmes=Field(dictionary,ProgrammeKeys);
  if(programmes!=null){string channel=Text(Field(dictionary,ChannelKeys));JsonContainer(programmes,channel==""?inherited:channel,epg,ref count,depth+1);return;}
  object channels=Field(dictionary,"channels");
  if(channels!=null) {
   if(IsArray(channels)) {
    foreach(object item in Array(channels)) {
     var channel=item as Dictionary<string,object>;if(channel==null)throw new InvalidDataException("EPG JSON channels трябва да съдържа обекти.");
     string id=Text(Field(channel,"id","channel","channel_id","tvg_id","tvg-id","epg_channel_id"));
     object listings=Field(channel,ProgrammeKeys);if(id==""||listings==null)throw new InvalidDataException("EPG JSON channels изисква id и programmes.");
     JsonContainer(listings,id,epg,ref count,depth+1);
    }
   } else JsonContainer(channels,inherited,epg,ref count,depth+1);
   return;
  }
  bool mapped=false;
  foreach(var pair in dictionary) {
   if(!IsArray(pair.Value))throw new InvalidDataException("Неподдържана EPG JSON схема: map трябва да е channelId -> масив. "+SupportedFormats);
   mapped=true;JsonContainer(pair.Value,pair.Key,epg,ref count,depth+1);
  }
  if(!mapped)throw new InvalidDataException("Неподдържана празна EPG JSON схема. "+SupportedFormats);
 }
 static void JsonRecord(object value,string inherited,Epg epg,ref int count) {
  if(++count>MaxProgrammes)throw new InvalidDataException("EPG надвишава лимита от 500 000 предавания.");
  var record=value as Dictionary<string,object>;if(record==null)throw new InvalidDataException("EPG JSON запис "+count+" трябва да е обект.");
  string channel=Text(Field(record,ChannelKeys));if(channel=="")channel=inherited;
  string title=Text(Field(record,TitleKeys)),description=Text(Field(record,DescriptionKeys));
  object encoded=Field(record,"title_base64");if(encoded!=null)title=DecodeBase64(Text(encoded),count);
  encoded=Field(record,"description_base64");if(encoded!=null)description=DecodeBase64(Text(encoded),count);
  Add(epg,channel,Field(record,StartKeys),Field(record,StopKeys),title,description,"JSON запис "+count);
 }
 static string DecodeBase64(string encoded,int row){try{return new UTF8Encoding(false,true).GetString(Convert.FromBase64String(encoded));}catch(FormatException){throw new InvalidDataException("Невалиден base64 текст в EPG JSON запис "+row+".");}catch(DecoderFallbackException){throw new InvalidDataException("Невалиден UTF-8 base64 текст в EPG JSON запис "+row+".");}}

 static int Column(string[] columns,string[] aliases) {for(int i=0;i<columns.Length;i++)foreach(string alias in aliases)if(String.Equals(columns[i].Trim(),alias,StringComparison.OrdinalIgnoreCase))return i;return -1;}
 static string Cell(string[] row,int column){return column>=0&&column<row.Length?row[column]:"";}
 static Epg ParseTsv(string data) {
  var result=new Epg();string[] header=null;int channel=-1,start=-1,stop=-1,title=-1,description=-1,line=0,count=0;
  foreach(string[] row in TsvRows(data)) {
   line++;if(row.All(String.IsNullOrWhiteSpace))continue;
   if(header==null){header=row;channel=Column(row,ChannelKeys);start=Column(row,StartKeys);stop=Column(row,StopKeys);title=Column(row,TitleKeys);description=Column(row,DescriptionKeys);if(channel<0||start<0||stop<0||title<0)throw new InvalidDataException("EPG TSV изисква заглавни колони channel, start, stop, title и незадължително description. "+SupportedFormats);continue;}
   if(++count>MaxProgrammes)throw new InvalidDataException("EPG надвишава лимита от 500 000 предавания.");
   if(row.Length!=header.Length)throw new InvalidDataException("EPG TSV ред "+line+" има различен брой колони от заглавния ред.");
   Add(result,Cell(row,channel),Cell(row,start),Cell(row,stop),Cell(row,title),Cell(row,description),"TSV ред "+line);
  }
  if(header==null)throw new InvalidDataException("EPG TSV няма заглавен ред.");Sort(result);return result;
 }
 // Quoted TSV fields may contain tabs/newlines; doubled quotes represent one quote.
 static IEnumerable<string[]> TsvRows(string text) {
  var cells=new List<string>();var field=new StringBuilder();bool quoted=false,afterQuote=false;
  for(int i=0;i<text.Length;i++) {
   char c=text[i];
   if(quoted){if(c=='"'){if(i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else{quoted=false;afterQuote=true;}}else field.Append(c);}
   else if(c=='\t'||c=='\r'||c=='\n') {
    cells.Add(field.ToString());if(cells.Count>256)throw new InvalidDataException("EPG TSV надвишава лимита от 256 колони.");field.Clear();afterQuote=false;
    if(c!='\t'){if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;yield return cells.ToArray();cells.Clear();}
    if(cells.Count>256)throw new InvalidDataException("EPG TSV надвишава лимита от 256 колони.");
   } else if(c=='"'&&field.Length==0&&!afterQuote)quoted=true;
   else {if(afterQuote)throw new InvalidDataException("Невалиден текст след затваряща кавичка в EPG TSV.");field.Append(c);}
   if(field.Length>1024*1024)throw new InvalidDataException("EPG TSV поле надвишава лимита от 1 MiB знака.");
  }
  if(quoted)throw new InvalidDataException("Незатворена кавичка в EPG TSV.");
  if(field.Length!=0||cells.Count!=0||afterQuote){cells.Add(field.ToString());if(cells.Count>256)throw new InvalidDataException("EPG TSV надвишава лимита от 256 колони.");yield return cells.ToArray();}
 }

 static DateTimeOffset ParseTime(object value) {
  if(value is DateTime){var d=(DateTime)value;return new DateTimeOffset(d.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(d,DateTimeKind.Utc):d).ToUniversalTime();}
  string text=Text(value).Trim();
  var xml=Regex.Match(text,@"^(\d{14})(?:\s*([+-])(\d{2})(\d{2}))?$");
  if(xml.Success) {
   DateTime date=DateTime.ParseExact(xml.Groups[1].Value,"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.None);
   int hours=xml.Groups[3].Success?Int32.Parse(xml.Groups[3].Value,CultureInfo.InvariantCulture):0,minutes=xml.Groups[4].Success?Int32.Parse(xml.Groups[4].Value,CultureInfo.InvariantCulture):0;
   if(minutes>59||hours>14||(hours==14&&minutes!=0))throw new FormatException("Невалидна часова зона.");
   var offset=new TimeSpan(hours,minutes,0);if(xml.Groups[2].Value=="-")offset=-offset;return new DateTimeOffset(date,offset);
  }
  if(Regex.IsMatch(text,@"^-?\d{1,16}(?:\.\d{1,7})?$")) {
   decimal timestamp;if(!Decimal.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out timestamp))throw new FormatException("Невалиден Unix час.");
   if(Math.Abs(timestamp)>=100000000000m)timestamp/=1000m;
   return Epoch.AddTicks(Decimal.ToInt64(timestamp*TimeSpan.TicksPerSecond));
  }
  if(!Regex.IsMatch(text,@"^\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?(?:[Zz]|[+-]\d{2}:?\d{2})?$"))throw new FormatException("Неподдържан час.");
  DateTimeOffset parsed;
  if(!DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out parsed))throw new FormatException("Невалиден ISO 8601 час.");return parsed;
 }
 static void Add(Epg epg,string channel,object start,object stop,string title,string description,string location) {
  channel=channel.Trim();if(channel=="")throw new InvalidDataException("EPG "+location+" няма channel/channel_id.");
  DateTimeOffset begins,ends;
  try{begins=ParseTime(start);ends=ParseTime(stop);}
  catch(Exception ex){if(!(ex is FormatException)&&!(ex is OverflowException)&&!(ex is ArgumentException))throw;throw new InvalidDataException("EPG "+location+" има невалидни start/stop. Използвайте ISO 8601, XMLTV или Unix час; без зона = UTC.",ex);}
  if(ends<=begins)throw new InvalidDataException("EPG "+location+" изисква stop след start.");
  DateTimeOffset now=DateTimeOffset.UtcNow;if(ends<now.AddDays(-1)||begins>now.AddDays(8))return;
  List<Programme> list;if(!epg.Items.TryGetValue(channel,out list)){list=new List<Programme>();epg.Items[channel]=list;}
  list.Add(new Programme{Channel=channel,Start=begins,Stop=ends,Title=title,Description=description});
 }
 static void Sort(Epg epg){foreach(var list in epg.Items.Values)list.Sort((a,b)=>a.Start.CompareTo(b.Start));}
}
}
