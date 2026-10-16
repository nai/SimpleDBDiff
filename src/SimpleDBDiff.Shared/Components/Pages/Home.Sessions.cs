using System.Text.Json;
using Microsoft.Extensions.Logging;
using SimpleDBDiff.Shared.Services;

namespace SimpleDBDiff.Shared.Components.Pages;

public partial class Home
{
    private ComparisonSession CaptureSession()
    {
        var name = ActiveSession!.Renamed ? ActiveSession.Name :
            !string.IsNullOrWhiteSpace(Left.Database) && !string.IsNullOrWhiteSpace(Right.Database)
                ? $"{Left.Database} → {Right.Database}" : ActiveSession.Name;
        return ActiveSession with
        {
            Name = name, Left = SessionConnection.From(Left), Right = SessionConnection.From(Right),
            Filters = new(Search, ShowEqual, ShowDifferent, ShowLeftOnly, ShowRightOnly),
            Selection = Selection is not null && Left.Snapshot is not null && Right.Snapshot is not null ? Selection.Capture(Left.Snapshot, Right.Snapshot) : ActiveSession.Selection
        };
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (ActiveSession is null || Restoring || Saving) return;
        var next = CaptureSession();
        var payload = JsonSerializer.Serialize(next);
        if (payload == LastSavedPayload || payload == LastAttemptedPayload) return;
        Saving = true; SaveStatus = "Saving…"; LastAttemptedPayload = payload; StateHasChanged();
        try
        {
            next = next with { UpdatedAt = DateTimeOffset.UtcNow };
            await SessionStore.SaveAsync(next);
            var savedSessions = await SessionStore.ListAsync();
            // Preserve any edits made while storage was writing; the next render saves them.
            ActiveSession = ActiveSession with { Name = next.Name, UpdatedAt = next.UpdatedAt };
            LastSavedPayload = JsonSerializer.Serialize(next); LastAttemptedPayload = null;
            SaveStatus = "Saved"; SaveFailed = false; Sessions = savedSessions;
        }
        catch (Exception ex) { Logger.LogError(ex, "Session autosave failed"); SaveStatus = "Session save failed. Retry to keep your changes."; SaveFailed = true; }
        finally { Saving = false; if (!Lifetime.IsCancellationRequested) StateHasChanged(); }
    }
    private void RetrySave() { LastAttemptedPayload = null; SaveFailed = false; }
    private async Task NewSessionAsync()
    {
        Left = new(); Right = new(); Search = ""; ShowEqual = ShowDifferent = ShowLeftOnly = ShowRightOnly = true;
        ClearComparison(); PendingSession = null;
        ActiveSession = new(Guid.NewGuid().ToString("N"), $"Session {Sessions.Count + 1}", false,
            SessionConnection.From(Left), SessionConnection.From(Right), new("", true,true,true,true), null, DateTimeOffset.UtcNow);
        LastSavedPayload = LastAttemptedPayload = null; SessionsOpen = false; SaveStatus = "Saving…";
        await Task.CompletedTask;
    }
    private async Task LoadSessionAsync(ComparisonSession session)
    {
        if (Saving) return;
        Restoring = true;
        try
        {
            ClearComparison(); Left.Password = Right.Password = "";
            Left = session.Left.ToEditor(); Right = session.Right.ToEditor();
            Search = session.Filters.Search; ShowEqual = session.Filters.Equal; ShowDifferent = session.Filters.Different;
            ShowLeftOnly = session.Filters.LeftOnly; ShowRightOnly = session.Filters.RightOnly;
            ActiveSession = PendingSession = session; LastSavedPayload = JsonSerializer.Serialize(session); LastAttemptedPayload = null;
            SaveStatus = "Session loaded · awaiting connections"; SessionsOpen = false;
            await RetrieveSessionPasswordsAsync();
            if (Vault.IsOpen && HasMatchingSavedPassword(Left) && HasMatchingSavedPassword(Right))
                await ContinueSessionAsync();
        }
        finally { Restoring = false; }
    }
    private bool HasMatchingSavedPassword(ConnectionEditorState side) => Profiles.Any(p => p.Name == side.SelectedProfile && p.HasSavedPassword && p.Host == side.Host && p.Port == side.Port && p.Database == side.Database && p.Username == side.Username);
    private async Task RetrieveSessionPasswordsAsync()
    {
        if (!Vault.IsOpen) return;
        foreach (var side in new[] { Left, Right })
        {
            var profile = Profiles.FirstOrDefault(p => p.Name == side.SelectedProfile);
            if (string.IsNullOrEmpty(side.Password) && profile is not null && profile.HasSavedPassword && profile.Host == side.Host && profile.Port == side.Port && profile.Database == side.Database && profile.Username == side.Username)
                side.Password = await ProfileStore.UnlockPasswordAsync(profile.Name) ?? "";
        }
    }
    private async Task ContinueSessionAsync()
    {
        if (PendingSession is null) return;
        Busy = Restoring = true; Script = null; Error = null;
        try
        {
            await RetrieveSessionPasswordsAsync();
            await Task.WhenAll(LoadSchemaCoreAsync(Left), LoadSchemaCoreAsync(Right));
            if (!CanCompare) { Error = "Session restored. Both databases must connect before comparison; check the connection errors and retry."; return; }
            CompareLoaded(PendingSession.Selection);
            ActiveSession = CaptureSession(); PendingSession = null; SaveStatus = "Saving…";
        }
        finally { Busy = Restoring = false; RefreshTables(); }
    }
    private void BeginRename(ComparisonSession session) { RenameId = session.Id; RenameName = session.Name; }
    private async Task RenameSessionAsync()
    {
        if (string.IsNullOrWhiteSpace(RenameName)) { Error = "Enter a session name."; return; }
        var session = ActiveSession?.Id == RenameId ? CaptureSession() : Sessions.FirstOrDefault(s => s.Id == RenameId);
        if (session is null) return;
        session = session with { Name = RenameName.Trim(), Renamed = true };
        try
        {
            await SessionStore.SaveAsync(session);
            if (ActiveSession?.Id == session.Id) ActiveSession = ActiveSession with { Name = session.Name, Renamed = true };
            Sessions = await SessionStore.ListAsync(); RenameId = null;
        }
        catch (Exception ex) { Logger.LogError(ex, "Session rename failed"); Error = "Could not rename the session."; }
    }
    private async Task DeleteSessionAsync(ComparisonSession session)
    {
        try
        {
            await SessionStore.DeleteAsync(session.Id);
            if (ActiveSession?.Id == session.Id) { ActiveSession = PendingSession = null; SaveStatus = "No session loaded"; }
            Sessions = await SessionStore.ListAsync();
        }
        catch (Exception ex) { Logger.LogError(ex, "Session delete failed"); Error = "Could not delete the session."; }
    }
}
