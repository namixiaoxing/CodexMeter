using System.Text.Json;
namespace CodexMeter;

internal static class UiDiagnostics
{
    public static void ComposerCheck()
    {
        using var fixture=new Form{ClientSize=new(1100,700),StartPosition=FormStartPosition.Manual,Location=new(60,70),ShowInTaskbar=false};
        using var editor=new Panel{Bounds=new(60,570,700,110)};
        using var send=new Button{Text="Send",Bounds=new(630,60,60,35)};
        editor.Controls.Add(send);fixture.Controls.Add(editor);fixture.Show();
        using var overlay=new OverlayForm(new Settings(),()=>{});
        HostSurface Host()=>HostWindow.ReadSurface(fixture.Handle,(uint)Environment.ProcessId)!;
        void Apply(){if(overlay.DockTo(Host())&&!overlay.Visible)overlay.Show();}
        overlay.GeometryChanged+=Apply;
        var deadline=Environment.TickCount64+8000;
        while(overlay.ComposerBounds is null&&Environment.TickCount64<deadline){Apply();Application.DoEvents();Thread.Sleep(10);}
        var initialized=overlay.ComposerBounds is not null&&!overlay.QuotaOnly;
        var samples=new List<object>();var passed=initialized;
        foreach(var width in new[]{870,970,700})
        {
            var host=Host();var origin=new Point(host.Client.Left+800,host.Client.Top+250);
            overlay.UpdateDragState(true,origin);
            var watch=System.Diagnostics.Stopwatch.StartNew();editor.Width=width;
            overlay.UpdateDragState(true,new(origin.X-20,origin.Y));
            var hiddenImmediately=!overlay.Visible&&!overlay.DockTo(Host());
            var hideMilliseconds=watch.Elapsed.TotalMilliseconds;
            watch.Restart();overlay.UpdateDragState(false,new(origin.X-20,origin.Y));
            var editorRight=editor.RectangleToScreen(editor.ClientRectangle).Right;
            bool Expected()=>overlay.ComposerBounds?.Right==editorRight&&(width==970?!overlay.Visible:overlay.Visible&&overlay.QuotaOnly==(width==870));
            // Completion must refresh placement without waiting for the 150/750 ms periodic path.
            while(!Expected()&&watch.ElapsedMilliseconds<2000){Application.DoEvents();Thread.Sleep(1);}
            watch.Stop();
            var success=hiddenImmediately&&hideMilliseconds<30&&Expected()&&watch.ElapsedMilliseconds<250;
            samples.Add(new{editorWidth=width,hiddenImmediately,hideMilliseconds,recoverMilliseconds=watch.Elapsed.TotalMilliseconds,overlay.Visible,overlay.QuotaOnly,success});
            passed&=success;
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"composer-check.json"),JsonSerializer.Serialize(new{initialized,passed,samples},new JsonSerializerOptions{WriteIndented=true}));
        if(!passed)Environment.ExitCode=1;
    }
    public static void FollowBenchmark()
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var fixture=new Form{ClientSize=new(900,650),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new(60,70)};
        fixture.Show();HostWindow.ShowPassive(fixture.Handle);
        using var overlay=new OverlayForm(new Settings(),()=>{});
        using var process=System.Diagnostics.Process.GetCurrentProcess();
        var host=HostWindow.ReadSurface(fixture.Handle,(uint)Environment.ProcessId)!;
        ComposerGeometry Geometry(HostSurface surface)=>new(new(surface.Client.Left+90,surface.Client.Bottom-130,surface.Client.Width-400,110),true);
        overlay.DockTo(host,Geometry(host));overlay.Show();HostWindow.ShowPassive(overlay.Handle);
        var fixtureSize=fixture.Size;
        object Run(bool eventDriven,int durationMs)
        {
            long updates=0,moves=0;
            using var motion=new HostMotionHook(surface=>
            {
                overlay.DockTo(surface,Geometry(surface));updates++;
            },SynchronizationContext.Current!);
            if(eventDriven)motion.Watch(host);
            Application.DoEvents();
            process.Refresh();
            var cpu=process.TotalProcessorTime.TotalMilliseconds;
            var paints=overlay.Paints;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            double nextMove=0,nextPoll=0;
            while(watch.Elapsed.TotalMilliseconds<durationMs)
            {
                var elapsed=watch.Elapsed.TotalMilliseconds;
                if(elapsed>=nextMove)
                {
                    var x=60+(int)(moves%120);var y=70+(int)(moves%32);
                    HostWindow.Position(fixture.Handle,new(new Point(x,y),fixtureSize));moves++;
                    nextMove=elapsed+1000d/120;
                }
                if(!eventDriven&&elapsed>=nextPoll)
                {
                    if(HostWindow.ReadSurface(fixture.Handle,(uint)Environment.ProcessId) is {} surface)
                    {
                        overlay.DockTo(surface,Geometry(surface));overlay.Invalidate();updates++;
                    }
                    nextPoll=elapsed+150;
                }
                Application.DoEvents();Thread.Sleep(1);
            }
            Application.DoEvents();watch.Stop();process.Refresh();
            var cpuMs=process.TotalProcessorTime.TotalMilliseconds-cpu;
            if(eventDriven&&(!motion.Active||updates<Math.Min(moves,motion.RefreshHz*watch.Elapsed.TotalSeconds)*.8))Environment.ExitCode=1;
            return new{mode=eventDriven?"monitor-paced":"150ms-polling",seconds=watch.Elapsed.TotalSeconds,moves,updates,
                sourceUpdatesPerSecond=moves/watch.Elapsed.TotalSeconds,followUpdatesPerSecond=updates/watch.Elapsed.TotalSeconds,
                paintCount=overlay.Paints-paints,cpuMs,cpuMachinePercent=cpuMs/(watch.Elapsed.TotalMilliseconds*Environment.ProcessorCount)*100,
                privateMiB=process.PrivateMemorySize64/1048576d,workingMiB=process.WorkingSet64/1048576d,hookActive=eventDriven&&motion.Active,
                displayHz=motion.RefreshHz,highResolution=motion.HighResolution,nativeDisplayRate=motion.NativeRefreshRate};
        }
        // Warm both paths before measuring so interop/JIT initialization is not charged to one mode.
        Run(false,750);Run(true,750);
        var samples=new[]{Run(false,5000),Run(true,5000),Run(true,5000),Run(false,5000)};
        var clocks=new List<object>();
        foreach(var hertz in new[]{59.94,120,144,240})
        {
            long pulses=0;using var clock=new MotionFrameClock(()=>Interlocked.Increment(ref pulses));clock.SetRate(hertz);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<1200){clock.Request();Thread.Sleep(10);}
            clock.Pause();watch.Stop();
            var actual=Interlocked.Read(ref pulses)/watch.Elapsed.TotalSeconds;
            clocks.Add(new{targetHz=hertz,actualHz=actual,clock.HighResolution});
            if(Math.Abs(actual-hertz)>hertz*.15)Environment.ExitCode=1;
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"follow-benchmark.json"),JsonSerializer.Serialize(new
        {logicalProcessors=Environment.ProcessorCount,requestedSourceHz=120,notes="Both modes drive the same synthetic test window; no user window is moved.",samples,clocks},new JsonSerializerOptions{WriteIndented=true}));
    }

    public static void AttachmentCheck()
    {
        using var fixture=new Form{ClientSize=new(900,650),ShowInTaskbar=false};
        var host=new HostSurface(fixture.Handle,(uint)Environment.ProcessId,new(100,100,900,650),true,false,96);
        var settings=new Settings();
        using var overlay=new OverlayForm(settings,()=>{});
        void Dock(HostSurface surface)=>overlay.DockTo(surface,geometry:new(new(surface.Client.X+90,surface.Client.Bottom-130,surface.Client.Width-400,110),true));
        Dock(host);overlay.Show();Dock(host);
        var before=HostWindow.ActualBounds(overlay.Handle);
        settings.ScalePercent=100;Dock(host);
        var independentOfPanel=before==HostWindow.ActualBounds(overlay.Handle);
        var focus=HostWindow.ForegroundHandle;
        var nativeOwner=HostWindow.OwnerOf(overlay.Handle)==fixture.Handle;
        var inside=true;
        foreach(var frame in new[]{new Rectangle(150,120,900,650),new Rectangle(150,120,1050,750),host.Client})
        {
            Dock(host with{Client=frame});inside&=frame.Contains(overlay.Bounds);
        }
        var focusPreserved=HostWindow.ForegroundHandle==focus;
        var full=HostWindow.ActualBounds(overlay.Handle);
        overlay.SetQuotaOnly(true);Dock(host);
        var quota=HostWindow.ActualBounds(overlay.Handle);
        var preservesAnchor=quota.Right==full.Right&&quota.Bottom==full.Bottom;
        overlay.SetQuotaOnly(false);Dock(host);
        var restored=HostWindow.ActualBounds(overlay.Handle)==full;
        var narrowEditor=new ComposerGeometry(new(host.Client.X+60,host.Client.Bottom-130,host.Client.Width-180,110),true);
        overlay.DockTo(host,narrowEditor);
        var shrinksBesideEditor=overlay.QuotaOnly&&!overlay.Bounds.IntersectsWith(narrowEditor.Bounds)&&overlay.Bottom==narrowEditor.Bounds.Bottom;
        var anchoredBounds=HostWindow.ActualBounds(overlay.Handle);
        var hidesWithoutRelocation=!overlay.DockTo(host,narrowEditor with{MatchesSize=false})&&!overlay.Visible&&HostWindow.ActualBounds(overlay.Handle)==anchoredBounds;
        hidesWithoutRelocation&=!overlay.DockTo(host,null)&&HostWindow.ActualBounds(overlay.Handle)==anchoredBounds;
        var blockedEditor=new ComposerGeometry(new(host.Client.X+60,host.Client.Bottom-130,host.Client.Width-80,110),true);
        hidesWithoutRelocation&=!overlay.DockTo(host,blockedEditor)&&HostWindow.ActualBounds(overlay.Handle)==anchoredBounds;
        Dock(host);
        var expandsWithRoom=!overlay.QuotaOnly&&HostWindow.ActualBounds(overlay.Handle)==full;
        using var panel=new MeterPanel(settings,()=>{},()=>{});
        panel.AdaptTo(host.Client,96);_=panel.Handle;
        // ShowInTaskbar=false may give WinForms an invisible helper owner; it must never be the host.
        var unowned=HostWindow.OwnerOf(panel.Handle)!=fixture.Handle&&!panel.TopMost;
        var deactivated=false;panel.Deactivate+=(_,_)=>deactivated=true;
        fixture.Show();HostWindow.ShowPassive(fixture.Handle);fixture.Activate();Application.DoEvents();
        panel.Show();panel.Activate();Application.DoEvents();
        var desktop=Screen.FromRectangle(panel.Bounds).WorkingArea;
        panel.AdaptTo(desktop,(uint)panel.DeviceDpi);
        var desktopCornerStable=true;
        var panelFrame=HostWindow.FrameBounds(panel.Handle);
        var corner=new Point(panelFrame.Right,panelFrame.Bottom);
        foreach(var percent in new[]{67,100,85,67})
        {
            settings.ScalePercent=percent;panel.ApplyScale();
            panelFrame=HostWindow.FrameBounds(panel.Handle);
            desktopCornerStable&=desktop.Contains(panelFrame)&&panelFrame.Right==corner.X&&panelFrame.Bottom==corner.Y&&panelFrame.Right==desktop.Right&&panelFrame.Bottom==desktop.Bottom;
        }
        var acquiredFocus=panel.ContainsFocus;
        HostWindow.ShowPassive(fixture.Handle);fixture.Activate();
        var deadline=Environment.TickCount64+500;
        while(panel.Visible&&Environment.TickCount64<deadline){Application.DoEvents();Thread.Sleep(10);}
        // Background launches can receive thread focus while Windows refuses foreground activation.
        // Only assert dismissal when an actual Deactivate event was observable.
        bool? dismissesOnBlur=deactivated?!panel.Visible:null;
        var report=new{vertical=full.Height>full.Width,overlayWidth=full.Width,overlayHeight=full.Height,quotaHeight=quota.Height,
            rightClearance=host.Client.Right-full.Right,bottomMargin=host.Client.Bottom-full.Bottom,nativeOwnerMatchesHost=nativeOwner,
            overlayNotTopmost=!overlay.TopMost,panelUnownedAndNotTopmost=unowned,panelAcquiredFocus=acquiredFocus,panelDismissesOnBlur=dismissesOnBlur,
            blurCheckStatus=deactivated?"observed":"foreground activation unavailable",
            panelScaleDoesNotMoveOverlay=independentOfPanel,attachedWithinClient=inside,stackingPreservesFocus=focusPreserved,
            quotaModeKeepsRightAndBottom=preservesAnchor,fullModeRestored=restored,shrinksBesideEditor,expandsWithRoom,
            desktopCornerStable,quotaWindowVertical=quota.Height>quota.Width*2,hidesWithoutRelocation,
            panelBounds=panel.Bounds,panelFrame=HostWindow.FrameBounds(panel.Handle),desktop};
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"attachment-check.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        if(!nativeOwner||!unowned||!independentOfPanel||!inside||!focusPreserved||!preservesAnchor||!restored||dismissesOnBlur==false||!shrinksBesideEditor||!expandsWithRoom||!desktopCornerStable||!hidesWithoutRelocation)Environment.ExitCode=1;
    }

    public static void Preview()
    {
        // Synthetic metrics keep layout previews independent of private logs, account data and settings.
        var now=DateTimeOffset.UtcNow;
        var token=new TokenSnapshot("preview-thread","gpt-6.1-sol",3772379,24837,3690000,113646,475000,82.4,now,824,10000,now.ToUnixTimeMilliseconds(),1);
        var quota=new QuotaSnapshot(new(10,300,now.AddHours(5).ToUnixTimeSeconds()),new(40,10080,now.AddDays(5).ToUnixTimeSeconds()),now,"合成预览数据");
        var money=new BudgetSnapshot(new(3.02m,2.8m,3.3m,10,24),new(14.34m,13m,16m,10,24),"API 等价美元 · 本机样本估算",24,1.23m);
        void Save(Form form,string name)
        {
            form.Show();Application.DoEvents();
            using var bitmap=new Bitmap(form.Width,form.Height);
            form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory,name+".png"));
            form.Hide();
        }
        foreach(var theme in new[]{"white","dark","miku"})
        {
            Ui.SetTheme(theme);
            var settings=new Settings{Theme=theme,ScalePercent=100};
            using var panel=new MeterPanel(settings,()=>{},()=>{});
            panel.AdaptTo(new(0,0,1920,1040),96);_=panel.Handle;
            panel.UpdateData(token,quota,"当前页面","额度接口 · 刚刚更新",money,1);
            Save(panel,"panel-preview-"+theme);
            foreach(var percent in new[]{67,85})
            {
                settings.ScalePercent=percent;panel.ApplyScale();Save(panel,$"panel-preview-{theme}-{percent}");
            }
            settings.ScalePercent=100;panel.ApplyScale();
            using var overlay=new OverlayForm(settings,()=>{});
            overlay.UpdateData(token,quota);overlay.PreviewSize(96);Save(overlay,"overlay-preview-"+theme);
            if(theme=="white")
            {
                var measurements=new List<object>();
                foreach(var scenario in new[]{("desktop",1920,1040,96),("laptop-125",1366,728,120),("laptop-150",1536,824,144),("narrow",600,460,144),("high-dpi",2560,1360,192)})
                {
                    var (name,width,height,dpi)=scenario;var area=new Rectangle(0,0,width,height);
                    panel.AdaptTo(area,(uint)dpi);Save(panel,"panel-preview-"+name);
                    overlay.PreviewSize(dpi,area);Save(overlay,"overlay-preview-"+name);
                    measurements.Add(new{name,dpi,panel=panel.LayoutInfo,panelSize=panel.Size,overlaySize=overlay.Size});
                }
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"layout-preview.json"),JsonSerializer.Serialize(measurements,new JsonSerializerOptions{WriteIndented=true}));
                overlay.SetQuotaOnly(true);overlay.PreviewSize(96);Save(overlay,"overlay-preview-quota-only");
                overlay.SetQuotaOnly(false);overlay.PreviewSize(96);
                overlay.UpdateData(null,null);Save(overlay,"overlay-preview-waiting");
                overlay.UpdateData(token,quota with{FiveHour=new(100,300,now.AddHours(1).ToUnixTimeSeconds()),Weekly=new(95,10080,now.AddDays(1).ToUnixTimeSeconds())});Save(overlay,"overlay-preview-low");
                overlay.UpdateData(token with{Input=999999999999,Output=999999999999,Tps=123456.7},quota with{FiveHour=new(0,300,now.AddHours(1).ToUnixTimeSeconds())});Save(overlay,"overlay-preview-large-values");
            }
        }
    }
}
