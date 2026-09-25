using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Ampm2.Sys;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Installing, Failed }

/// <summary>
/// The About section's update panel, shared by both apps. Call its methods on the UI thread: awaits resume
/// there, so property changes are raised on the UI thread too.
/// </summary>
public sealed class UpdateModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private UpdateState _state = UpdateState.Idle;
    private UpdateRelease? _release;
    private string _error = "";
    private double _progress = -1;
    private DateTime _checkedAt;

    public UpdateState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            RaiseAll();
        }
    }

    public UpdateRelease? Release => _release;
    public string LatestVersion => _release?.Version.ToString(3) ?? "";
    public string CurrentVersion => AppInfo.Version;

    public bool IsBusy => _state is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
    public bool IsAvailable => _state == UpdateState.Available || (_state == UpdateState.Failed && _release != null && _release.Version > Updater.Current);
    public bool CanCheck => _state is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Failed;
    public bool CanInstallHere => Updater.CanInstallHere(out _);
    public bool CanInstallNow => IsAvailable && CanInstallHere && Updater.Package(_release!) != null;
    public bool CanDownloadOnly => IsAvailable && !CanInstallNow;
    public bool HasReleasePage => _release != null && _state is UpdateState.Available or UpdateState.UpToDate or UpdateState.Failed;
    public bool ShowProgress => IsBusy;
    public bool ShowActions => !IsBusy;
    public bool ProgressUnknown => _state != UpdateState.Downloading || _progress < 0;
    public double Progress => Math.Max(0, _progress) * 100;
    public bool IsError => _state == UpdateState.Failed;
    public bool IsGood => _state == UpdateState.UpToDate;
    public string CheckText => _state == UpdateState.Idle ? "Check for updates" : _state == UpdateState.Failed ? "Try again" : "Check again";
    public string InstallText => $"Install {LatestVersion} and restart";
    public string ReleasePage => _release?.PageUrl ?? AppInfo.GitHubUrl + "/releases/latest";

    public string StatusText => _state switch
    {
        UpdateState.Idle => "Updates",
        UpdateState.Checking => "Checking for updates…",
        UpdateState.UpToDate => "ampm2 is up to date",
        UpdateState.Available => $"Version {LatestVersion} is available",
        UpdateState.Downloading => _progress >= 0 ? $"Downloading {LatestVersion}… {Math.Round(_progress * 100)}%" : $"Downloading {LatestVersion}…",
        UpdateState.Installing => $"Installing {LatestVersion}…",
        _ => "Couldn't update",
    };

    public string Detail => _state switch
    {
        UpdateState.Idle => "Checks GitHub for a newer version of ampm2.",
        UpdateState.Checking => "Asking GitHub for the latest release.",
        UpdateState.UpToDate => _release != null && _release.Version < Updater.Current
            ? $"You have {CurrentVersion}, newer than the latest release ({LatestVersion})."
            : $"{CurrentVersion} is the latest version.",
        UpdateState.Available => CanInstallNow
            ? $"You have {CurrentVersion}. Installing restarts ampm2; your pm2 processes keep running."
            : Updater.CanInstallHere(out var why) ? $"This release has no {Updater.PackageName(_release!.Version)}. Download it from the release page." : why,
        UpdateState.Downloading => "The download is checked against the release's SHA256 checksums before anything is installed.",
        UpdateState.Installing => Updater.Target == UpdateTarget.WindowsInstaller
            ? "ampm2 closes now and reopens when the installer finishes."
            : "ampm2 closes now and reopens in a moment.",
        _ => _error,
    };

    /// <summary>Checks GitHub. <paramref name="quiet"/> (opening About) skips it when a check ran in the last 30 minutes.</summary>
    public async Task CheckAsync(bool quiet = false)
    {
        if (!CanCheck) return;
        if (quiet && (_state == UpdateState.Available || (_state == UpdateState.UpToDate && DateTime.UtcNow - _checkedAt < TimeSpan.FromMinutes(30)))) return;
        State = UpdateState.Checking;
        try
        {
            _release = await Updater.FetchLatestAsync();
            _checkedAt = DateTime.UtcNow;
            State = _release.Version > Updater.Current ? UpdateState.Available : UpdateState.UpToDate;
        }
        catch (Exception ex) { Fail(ex); }
        RaiseAll();
    }

    /// <summary>Downloads and verifies the update, starts the installer helper, then calls <paramref name="quit"/>.</summary>
    public async Task InstallAsync(Action quit)
    {
        if (!CanInstallNow || _release == null) return;
        var release = _release;
        _progress = -1;
        State = UpdateState.Downloading;
        try
        {
            var progress = new Progress<double>(p =>
            {
                _progress = p;
                Raise(nameof(Progress));
                Raise(nameof(ProgressUnknown));
                Raise(nameof(StatusText));
            });
            var file = await Updater.DownloadAsync(release, progress);
            State = UpdateState.Installing;
            await Task.Delay(400);   // let "Installing…" paint before the window goes away
            Updater.StartInstall(file, release);
            await Task.Delay(300);
            quit();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Fail(Exception ex)
    {
        _error = ex is UpdateException ? ex.Message : "Something went wrong: " + ex.Message;
        State = UpdateState.Failed;
        RaiseAll();
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
