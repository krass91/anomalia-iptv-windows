using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
namespace IptvWindows {
public static class AndroidBackup {
 public static string SavePlaylist(Storage store,Profile p,List<Channel> items){Directory.CreateDirectory(Path.Combine(store.Root,"playlists"));string rel="playlists/"+p.Id+".m3u";File.WriteAllText(Path.Combine(store.Root,rel),Playlist.Export(items),new UTF8Encoding(true));return rel;}
 public static int Import(object obj,Settings settings,Storage store){
  var raw=Provider.V(obj,"channels") as object[];var pls=Provider.V(obj,"playlists") as object[];if(raw==null&&pls==null)throw new InvalidDataException("Архивът няма channels или playlists.");raw=raw??new object[0];pls=pls??new object[0];
  var imports=new List<Profile>();int total=0;
  if(pls.Length==0)pls=new object[]{new Dictionary<string,object>{{"id",""},{"name","Архив от Android"}}};
  foreach(var item in pls){string type=Provider.S(item,"sourceType"),source=Provider.S(item,"sourceUrlOrPath"),oldId=Provider.S(item,"id");if(type=="DEMO")continue;var p=new Profile{Name=Provider.S(item,"name"),Type="file"};if(p.Name=="")p.Name="Архив от Android";
   if(type=="XTREAM"){var parts=source.Split('|');if(parts.Length>=3){p.Type="xtream";p.Address=parts[0];p.Username=parts[1];p.Password=String.Join("|",parts.Skip(2));}}
   else if(type=="STALKER"){int split=source.LastIndexOf('|');if(split>0){p.Type="portal";p.Address=source.Substring(0,split);p.Mac=source.Substring(split+1);}}
   else if(type=="URL"&&source.StartsWith("http",StringComparison.OrdinalIgnoreCase)){p.Type="m3u";p.Address=source;}
   var list=new List<Channel>();foreach(var c in raw){if(oldId!=""&&Provider.S(c,"playlistId")!=oldId)continue;string url=Provider.S(c,"streamUrl");if(url=="")url=Provider.S(c,"url");if(url=="")continue;var ch=new Channel{Name=Provider.S(c,"name"),Url=url,Group=Provider.S(c,"groupTitle"),TvgId=Provider.S(c,"epgId"),Logo=Provider.S(c,"logoUrl"),Kind=p.Type=="portal"?"portal":"live",Id=Provider.S(c,"id")};list.Add(ch);if(Provider.S(c,"isFavorite").Equals("True",StringComparison.OrdinalIgnoreCase)&&!settings.Favorites.Contains(ch.Key))settings.Favorites.Add(ch.Key);}
   if(list.Count==0&&type=="TEXT")list=Playlist.Parse(source,"");
   if(p.Type=="file"){if(list.Count==0)continue;p.Address=SavePlaylist(store,p,list);}store.Write("channels-"+p.Id+".json",list);imports.Add(p);total+=list.Count;
  }
  if(imports.Count==0)throw new InvalidDataException("Не са намерени възстановими профили или канали.");settings.Profiles.AddRange(imports);settings.LastProfile=imports[0].Id;return total;
 }
}
}
