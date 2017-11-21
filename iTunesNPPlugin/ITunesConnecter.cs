﻿using iTunesLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace iTunesNPPlugin
{
    public class ITunesConnecter
    {
        //iTunes instance
        private iTunesApp app;

        private bool isITunesInitialized = false;
        private int lastTrackID;
        private SemaphoreSlim sem = new SemaphoreSlim(1, 1);
        private bool itunesComOK = false;
        private Task songupdatewatchertask;

        public static void CreateITunesInstance()
        {
            new ITunesConnecter();
        }

        public ITunesConnecter()
        {
            Task.Run(() => ITunesQuitWatcher());
            Debug.WriteLine("[DEBUG]itunes watcher initialized.");
        }

        private void SongUpdateWatcher()
        {
            while (true)
            {
                //wait
                Thread.Sleep(5000);
                sem.Wait();
                if (!isITunesInitialized) return;
                sem.Release();
                app = new iTunesApp();
                Debug.WriteLine("[DEBUG]itunes COM initialized.");
                var ctrack = app.CurrentTrack;
                if (app.CurrentTrack == null || ctrack.TrackDatabaseID == lastTrackID)
                {
                    if (ctrack != null) Marshal.FinalReleaseComObject(ctrack);
                    Marshal.FinalReleaseComObject(app);
                    continue;
                }
                //song changed
                OnPlayerPlayingTrackChangedEvent();
                lastTrackID = ctrack.TrackDatabaseID;
                Marshal.FinalReleaseComObject(ctrack);
                Marshal.FinalReleaseComObject(app);
            }
        }

        private void ITunesQuitWatcher()
        {
            while ((Win32API.FindWindow("iTunesApp", "iTunes") == IntPtr.Zero ||
                   Win32API.FindWindow("iTunes", "iTunes") == IntPtr.Zero) && Win32API.IsWindowVisible(Win32API.FindWindow("iTunes", "iTunes")) == false)
            {
                Thread.Sleep(1000);
            }
            Debug.WriteLine("[DEBUG]itunes start wait end.");
            isITunesInitialized = true;
            //test itunes com
            var testitunes = new iTunesApp();
            Marshal.FinalReleaseComObject(testitunes);
            while (true)
            {
                sem.Wait();
                if (songupdatewatchertask == null) songupdatewatchertask = Task.Run(() => SongUpdateWatcher());
                Thread.Sleep(100);
                if (Win32API.FindWindow("iTunesApp", "iTunes") == IntPtr.Zero ||
                    Win32API.FindWindow("iTunes", "iTunes") == IntPtr.Zero || Win32API.IsWindowVisible(Win32API.FindWindow("iTunes", "iTunes")) == false)
                {
                    Debug.WriteLine("[DEBUG]itunes quit event.");
                    OnQuittingEvent();
                    sem.Release();
                    return;
                }
                sem.Release();
                Thread.Sleep(100);
            }
        }

        private void OnQuittingEvent()
        {
            isITunesInitialized = false;
            //wait until itunes exits.
            while (true)
            {
                if (Process.GetProcessesByName("iTunes").Count() == 0)
                {
                    Debug.WriteLine("[DEBUG]itunes quit ok.");
                    ITunesWatcher.CreateWatcherTask();
                    return;
                }
                Thread.Sleep(1000);
            }
        }

        private void OnPlayerPlayingTrackChangedEvent()
        {
            var sendmap = new Dictionary<string, string>();
            var track = (iTunesLib.IITFileOrCDTrack) app.CurrentTrack;
            sendmap.Add("title", track.Name);
            sendmap.Add("albumartist", track.AlbumArtist);
            sendmap.Add("artist", track.Artist);
            sendmap.Add("trackcount", new StringBuilder(track.PlayedCount).ToString());
            sendmap.Add("album", track.Album);
            sendmap.Add("composer", track.Composer);
            var artworkcoll = track.Artwork;
            if (artworkcoll.Count > 0)
            {
                var artwork = track.Artwork[1];
                artwork.SaveArtworkToFile(AppDomain.CurrentDomain.BaseDirectory + "/artwork.png");
                Marshal.FinalReleaseComObject(artwork);
                var imgbary = System.IO.File.ReadAllBytes(AppDomain.CurrentDomain.BaseDirectory + "/artwork.png");
                sendmap.Add("albumart", Convert.ToBase64String(imgbary));
                System.IO.File.Delete(AppDomain.CurrentDomain.BaseDirectory + "/artwork.png");
            }
            Marshal.FinalReleaseComObject(artworkcoll);
            Marshal.FinalReleaseComObject(track);
            var json = new JavaScriptSerializer().Serialize(sendmap.ToDictionary(item => item.Key.ToString(),
                item => item.Value?.ToString() ?? ""));
            Debug.WriteLine(json);
            Task.Run(() =>
            {
                try
                {
                    var bary = Encoding.UTF8.GetBytes(json);
                    var pipe = new NamedPipeClientStream("NowPlayingTunesV2PIPE");
                    pipe.Connect(1000); //set timeout 1000msec.
                    pipe.Write(bary, 0, bary.Count());
                    pipe.Close();
                }
                catch
                {
                }
            });
        }
    }
}