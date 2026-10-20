// Local synthetic checks, only invoked with --video-test and --suite.
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace IptvWindows {
public partial class MainWindow {
 async Task<bool> AdditionalTests(Dictionary<string,object> result,string path,string output){
  var checks=new Dictionary<string,bool>();
  string file=new Uri(Path.GetFullPath(path)).AbsoluteUri;
  ToggleMulti();
  for(int i=1;i<4;i++){SelectPane(i);await Play(new Channel{Name="Тестов екран "+(i+1),Url=file});}
  await Task.Delay(2400);checks["four_simultaneous_video_outputs"]=players.All(p=>p!=null&&p.Playing&&p.HasVideo);
  SelectPane(2);await Task.Delay(400);result["multiVolumeValues"]=players.Select(p=>p.Volume).ToArray();checks["selected_screen_audio_only"]=players[2].Volume==settings.Volume&&players[0].Volume==0&&players[1].Volume==0&&players[3].Volume==0;
  string folder=Path.GetDirectoryName(output);for(int i=0;i<4;i++){players[i].Snapshot(Path.Combine(folder,"multi-screen-"+i+".png"));}
  ToggleMulti();checks["single_screen_stops_other_players"]=players.Skip(1).All(p=>!p.Playing);
  string srt=Path.Combine(folder,"test-subtitles.srt");File.WriteAllText(srt,"1\r\n00:00:00,000 --> 00:00:22,000\r\nТестови субтитри на български\r\n",new UTF8Encoding(true));players[0].Subtitle(srt);await Task.Delay(1000);checks["external_subtitle_track"]=players[0].Tracks(false).Any(t=>t.Id>=0);
  ToggleFullscreen();checks["fullscreen_enters"]=fullscreen&&!sidebar.Visible&&FormBorderStyle==System.Windows.Forms.FormBorderStyle.None;ToggleFullscreen();checks["fullscreen_restores"]=!fullscreen&&sidebar.Visible;
  settings.Theme="LIGHT";ApplyTheme();checks["light_theme"]=BackColor.GetBrightness()>0.7;settings.Theme="DARK";ApplyTheme();checks["dark_theme"]=BackColor.GetBrightness()<0.2;
  string android=Storage.Json().Serialize(new{playlists=new[]{new{id=77,name="Тестова библиотека",sourceType="FILE",sourceUrlOrPath="/unavailable/android/list.m3u"}},channels=new[]{new{id=11,playlistId=77,name="Български тест",streamUrl=file,groupTitle="Тестова група",epgId="test-bg",logoUrl="",isFavorite=true},new{id=12,playlistId=77,name="Втори канал",streamUrl=file+"?second",groupTitle="Друга група",epgId="",logoUrl="",isFavorite=false}}});
  checks["android_backup_import_count"]=AndroidBackup.Import(Storage.Json().DeserializeObject(android),settings,store)==2;Save();RefreshProfiles();checks["library_after_import"]=channels.Count==2&&profiles.Items.Count>0;
  search.Text="Български";checks["search_cyrillic"]=visible.Count==1;search.Text="";onlyFavorites=true;Filter();checks["favorite_filter"]=visible.Count==1;onlyFavorites=false;Filter();groups.SelectedItem="Друга група";checks["group_filter"]=visible.Count==1&&visible[0].Name=="Втори канал";groups.SelectedItem="Всички групи";
  var loaded=await new Provider(settings,App.Root).Load(current);checks["portable_relative_playlist_load"]=loaded.Count==2;
  var reloaded=store.Read("settings.json",new Settings());checks["settings_persist"]=reloaded.Profiles.Count==settings.Profiles.Count&&reloaded.Favorites.Count==settings.Favorites.Count;
  var exported=Playlist.Export(channels);checks["m3u_export_roundtrip"]=Playlist.Parse(exported,"").Count==2;
  string backup=Storage.Json().Serialize(new Backup{settings=settings,caches=new Dictionary<string,System.Collections.Generic.List<Channel>>{{current.Id,channels}}});var restored=Storage.Json().Deserialize<Backup>(backup);checks["windows_backup_roundtrip"]=restored.settings.Profiles.Count==settings.Profiles.Count&&restored.caches[current.Id].Count==2;
  checks["library_visible"]=list.Width>200&&list.Height>150&&screens.Width>500&&screens.Height>180;
  result["uiChecks"]=checks;result["uiChecksPassed"]=checks.Values.Count(v=>v);result["uiChecksTotal"]=checks.Count;
  using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(0,0,Width,Height));image.Save(Path.Combine(folder,"app-library-window.png"));}
  return checks.Values.All(v=>v);
 }
}
}
