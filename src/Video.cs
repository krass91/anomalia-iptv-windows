// GPL-2.0-or-later
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Security.Cryptography;

namespace IptvWindows {
public static class Runtime {
 [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool SetDllDirectory(string path);
 public static string Prepare(string root) {
  string target=Path.Combine(root,"runtime-vlc-3.0.24"), marker=Path.Combine(target,"complete.txt");
  if(!File.Exists(marker)||!File.Exists(Path.Combine(target,"libvlc.dll"))){
   Directory.CreateDirectory(target);
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("vlc.zip")){
    if(stream==null)throw new FileNotFoundException("Липсва вграденият видео двигател.");
    using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))foreach(var entry in zip.Entries){
     int slash=entry.FullName.IndexOf('/');if(slash<0)continue; string rel=entry.FullName.Substring(slash+1).Replace('/',Path.DirectorySeparatorChar);if(rel==""||entry.FullName.EndsWith("/"))continue;
     string path=Path.GetFullPath(Path.Combine(target,rel));if(!path.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Невалиден път във видео пакета.");
     Directory.CreateDirectory(Path.GetDirectoryName(path));using(var source=entry.Open())using(var dest=File.Create(path))source.CopyTo(dest);
    }
   }
   File.WriteAllText(marker,"VLC 3.0.24 / embedded official win64 ZIP");
  }
  Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH",Path.Combine(target,"plugins"));SetDllDirectory(target);return target;
 }
}
public class Track {public int Id;public string Name;public override string ToString(){return Name;}}
public sealed class Video : IDisposable {
 const string D="libvlc.dll"; const CallingConvention C=CallingConvention.Cdecl;
 [DllImport(D,CallingConvention=C)] static extern IntPtr libvlc_new(int argc,IntPtr argv);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_release(IntPtr inst);
 [DllImport(D,CallingConvention=C)] static extern IntPtr libvlc_media_player_new(IntPtr inst);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_media_player_release(IntPtr player);
 [DllImport(D,CallingConvention=C)] static extern IntPtr libvlc_media_new_location(IntPtr inst,byte[] location);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_media_add_option(IntPtr m,byte[] option);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_media_release(IntPtr m);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_media_player_set_media(IntPtr p,IntPtr m);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_media_player_set_hwnd(IntPtr p,IntPtr hwnd);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_media_player_play(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_media_player_stop(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_media_player_set_pause(IntPtr p,int pause);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_media_player_is_playing(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_media_player_get_state(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern long libvlc_media_player_get_time(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern long libvlc_media_player_get_length(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_media_player_set_time(IntPtr p,long time);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_audio_set_volume(IntPtr p,int volume);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_audio_get_volume(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_video_set_aspect_ratio(IntPtr p,byte[] aspect);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_video_set_scale(IntPtr p,float scale);
 [DllImport(D,CallingConvention=C)] static extern IntPtr libvlc_audio_get_track_description(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern IntPtr libvlc_video_get_spu_description(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_track_description_list_release(IntPtr list);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_audio_set_track(IntPtr p,int id);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_video_set_spu(IntPtr p,int id);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_video_take_snapshot(IntPtr p,uint num,byte[] path,uint width,uint height);
 [DllImport(D,CallingConvention=C)] static extern int libvlc_media_player_add_slave(IntPtr p,int type,byte[] uri,bool select);
 [DllImport(D,CallingConvention=C)] static extern uint libvlc_media_player_has_vout(IntPtr p);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_video_set_mouse_input(IntPtr p,int on);
 [DllImport(D,CallingConvention=C)] static extern void libvlc_video_set_key_input(IntPtr p,int on);
 IntPtr inst,player;bool paused;
 static byte[] U(string s){return Encoding.UTF8.GetBytes((s??"")+"\0");}
 public Video(IntPtr hwnd){
  string[] args={"--ignore-config","--aout=directsound","--no-video-title-show","--no-osd","--no-stats","--no-media-library","--no-one-instance","--no-plugins-cache","--quiet"};
  IntPtr argv=Marshal.AllocHGlobal(IntPtr.Size*args.Length);var ptrs=new List<IntPtr>();
  try{for(int i=0;i<args.Length;i++){byte[] b=U(args[i]);IntPtr p=Marshal.AllocHGlobal(b.Length);Marshal.Copy(b,0,p,b.Length);Marshal.WriteIntPtr(argv,i*IntPtr.Size,p);ptrs.Add(p);}inst=libvlc_new(args.Length,argv);}finally{foreach(var p in ptrs)Marshal.FreeHGlobal(p);Marshal.FreeHGlobal(argv);}
  if(inst==IntPtr.Zero)throw new InvalidOperationException("Видео двигателят не се зареди.");player=libvlc_media_player_new(inst);if(player==IntPtr.Zero)throw new InvalidOperationException("Плеърът не се създаде.");SetWindow(hwnd);libvlc_video_set_mouse_input(player,0);libvlc_video_set_key_input(player,0);
 }
 public void SetWindow(IntPtr hwnd){libvlc_media_player_set_hwnd(player,hwnd);}
 public void Play(string url,Channel c,Settings s){Stop();IntPtr media=libvlc_media_new_location(inst,U(url));if(media==IntPtr.Zero)throw new InvalidOperationException("Невалиден медиен адрес.");try{libvlc_media_add_option(media,U(":network-caching="+s.BufferMs));libvlc_media_add_option(media,U(":live-caching="+s.BufferMs));libvlc_media_add_option(media,U(":file-caching=500"));libvlc_media_add_option(media,U(":avcodec-hw="+(s.Hardware?"any":"none")));string agent=c.UserAgent==""?s.UserAgent:c.UserAgent;if(agent!="")libvlc_media_add_option(media,U(":http-user-agent="+agent));if(c.Referer!="")libvlc_media_add_option(media,U(":http-referrer="+c.Referer));libvlc_media_player_set_media(player,media);}finally{libvlc_media_release(media);}if(libvlc_media_player_play(player)<0)throw new IOException("Потокът не може да се отвори.");paused=false;Volume=s.Volume;Aspect(s.Aspect);}
 public void Stop(){if(player!=IntPtr.Zero)libvlc_media_player_stop(player);paused=false;}
 public void Pause(){paused=!paused;libvlc_media_player_set_pause(player,paused?1:0);}
 public bool Playing{get{return libvlc_media_player_is_playing(player)!=0;}}
 public int State{get{return libvlc_media_player_get_state(player);}}
 public long Time{get{return libvlc_media_player_get_time(player);}set{libvlc_media_player_set_time(player,value);}}
 public long Length{get{return libvlc_media_player_get_length(player);}}
 public int Volume{get{return libvlc_audio_get_volume(player);}set{libvlc_audio_set_volume(player,value);}}
 public bool HasVideo{get{return libvlc_media_player_has_vout(player)>0;}}
 public void Aspect(string aspect){libvlc_video_set_scale(player,0);libvlc_video_set_aspect_ratio(player,aspect=="16:9"?U("16:9"):aspect=="4:3"?U("4:3"):null);}
 public void Snapshot(string path){if(libvlc_video_take_snapshot(player,0,U(path),0,0)!=0)throw new IOException("Няма видео кадър за снимка.");}
 public void Subtitle(string path){if(libvlc_media_player_add_slave(player,0,U(new Uri(path).AbsoluteUri),true)!=0)throw new IOException("Субтитрите не се заредиха.");}
 public List<Track> Tracks(bool audio){IntPtr first=audio?libvlc_audio_get_track_description(player):libvlc_video_get_spu_description(player),p=first;var list=new List<Track>();try{while(p!=IntPtr.Zero&&list.Count<100){int id=Marshal.ReadInt32(p);IntPtr name=Marshal.ReadIntPtr(p,8);string text="";if(name!=IntPtr.Zero){int n=0;while(Marshal.ReadByte(name,n)!=0&&n<4096)n++;byte[] b=new byte[n];Marshal.Copy(name,b,0,n);text=Encoding.UTF8.GetString(b);}list.Add(new Track{Id=id,Name=text});p=Marshal.ReadIntPtr(p,16);}}finally{if(first!=IntPtr.Zero)libvlc_track_description_list_release(first);}return list;}
 public void SelectTrack(bool audio,int id){if(audio)libvlc_audio_set_track(player,id);else libvlc_video_set_spu(player,id);}
 public void Dispose(){if(player!=IntPtr.Zero){Stop();libvlc_media_player_release(player);player=IntPtr.Zero;}if(inst!=IntPtr.Zero){libvlc_release(inst);inst=IntPtr.Zero;}}
}
}
