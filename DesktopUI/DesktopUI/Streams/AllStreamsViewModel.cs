﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MaterialDesignThemes.Wpf;
using Speckle.Core.Api;
using Speckle.Core.Logging;
using Speckle.DesktopUI.Utils;
using Stylet;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace Speckle.DesktopUI.Streams
{
  public class AllStreamsViewModel : Screen, IHandle<StreamAddedEvent>, IHandle<StreamUpdatedEvent>, IHandle<
    StreamRemovedEvent>, IHandle<ApplicationEvent>, IHandle<ReloadRequestedEvent>
  {
    private readonly IViewManager _viewManager;
    private readonly IStreamViewModelFactory _streamViewModelFactory;
    private readonly IDialogFactory _dialogFactory;
    private readonly IEventAggregator _events;
    private readonly ConnectorBindings _bindings;

    private StreamsRepository _repo;
    private BindableCollection<StreamState> _streamList;
    private Stream _selectedStream;
    private Branch _selectedBranch;
    private CancellationTokenSource _cancellationToken = new CancellationTokenSource();

    public BindableCollection<StreamState> StreamList
    {
      get => _streamList;
      set => SetAndNotify(ref _streamList, value);
    }

    public Stream SelectedStream
    {
      get => _selectedStream;
      set => SetAndNotify(ref _selectedStream, value);
    }

    public Branch SelectedBranch
    {
      get => _selectedBranch;
      set => SetAndNotify(ref _selectedBranch, value);
    }


    public AllStreamsViewModel(IViewManager viewManager, IStreamViewModelFactory streamViewModelFactory, IDialogFactory dialogFactory, IEventAggregator events, StreamsRepository streamsRepo, ConnectorBindings bindings)
    {
      _repo = streamsRepo;
      _events = events;
      DisplayName = bindings.GetApplicationHostName() + " streams";
      _viewManager = viewManager;
      _streamViewModelFactory = streamViewModelFactory;
      _dialogFactory = dialogFactory;
      _bindings = bindings;

      StreamList = LoadStreams();

      _events.Subscribe(this);
    }

    private BindableCollection<StreamState> LoadStreams()
    {
      var streams = new BindableCollection<StreamState>(_bindings.GetFileContext());

      if (streams.Count == 0)
        streams = _repo.LoadTestStreams();

      return streams;
    }

    public void ShowStreamInfo(StreamState state)
    {
      Tracker.TrackPageview(Tracker.STREAM_DETAILS);
      var item = _streamViewModelFactory.CreateStreamViewModel();
      item.StreamState = state;

      // get main branch for now
      // TODO allow user to select branch
      item.Branch = _repo.GetMainBranch(state.Stream.branches.items);

      ((RootViewModel)Parent).GoToStreamViewPage(item);
    }

    public void SwapState(StreamState state)
    {
      state.Stream = state.Stream;
      StreamList.Refresh();
      state.IsSenderCard = !state.IsSenderCard;
    }

    public async void ShowStreamUpdateNameDialog(StreamState state)
    {
      Tracker.TrackPageview("stream", "dialog-update");
      var viewmodel = _dialogFactory.CreateStreamUpdateDialog();
      viewmodel.StreamState = state;
      viewmodel.SelectedSlide = 0;
      var view = _viewManager.CreateAndBindViewForModelIfNecessary(viewmodel);

      await DialogHost.Show(view, "RootDialogHost");
    }

    public async void ShowStreamUpdateObjectsDialog(StreamState state)
    {
      Tracker.TrackPageview("stream", "dialog-update");
      var viewmodel = _dialogFactory.CreateStreamUpdateDialog();
      viewmodel.StreamState = state;
      viewmodel.SelectedSlide = 1;
      var view = _viewManager.CreateAndBindViewForModelIfNecessary(viewmodel);

       await DialogHost.Show(view, "RootDialogHost");
    }

    public async void ShowStreamUpdateCollabsDialog(StreamState state)
    {
      Tracker.TrackPageview("stream", "dialog-update");
      var viewmodel = _dialogFactory.CreateStreamUpdateDialog();
      viewmodel.StreamState = state;
      viewmodel.SelectedSlide = 2;
      var view = _viewManager.CreateAndBindViewForModelIfNecessary(viewmodel);

      await DialogHost.Show(view, "RootDialogHost");
    }

    public async void Send(StreamState state)
    {
      state.IsSending = true;
      Tracker.TrackPageview(Tracker.SEND);
      _cancellationToken = new CancellationTokenSource();
      state.CancellationToken = _cancellationToken.Token;

      var res = await Task.Run(() => _repo.ConvertAndSend(state));
      if (res != null)
      {
        var index = StreamList.IndexOf(state);
        StreamList[index] = res;
        StreamList.Refresh();
      }

      state.Progress.ResetProgress();
      state.IsSending = false;
    }

    public async void Receive(StreamState state)
    {
      state.IsReceiving = true;
      Tracker.TrackPageview(Tracker.RECEIVE);
      _cancellationToken = new CancellationTokenSource();
      state.CancellationToken = _cancellationToken.Token;

      var res = await Task.Run(() => _repo.ConvertAndReceive(state));
      if (res != null)
      {
        state = res;
        StreamList.Refresh();
      }

      state.Progress.ResetProgress();
      state.IsReceiving = false;
    }

    public void CancelToken()
    {
      _cancellationToken.Cancel();
    }

    public async void ShowStreamCreateDialog()
    {
      Tracker.TrackPageview("stream", "dialog-add");
      var viewmodel = _dialogFactory.CreateStreamCreateDialog();
      viewmodel.StreamIds = StreamList.Select(s => s.Stream.id).ToList();
      var view = _viewManager.CreateAndBindViewForModelIfNecessary(viewmodel);

      var result = await DialogHost.Show(view, "RootDialogHost");
    }

    public async void ShowShareDialog(StreamState state)
    {
      Tracker.TrackPageview("stream", "dialog-share");
      var viewmodel = _dialogFactory.CreateShareStreamDialogViewModel();
      viewmodel.StreamState = state;
      var view = _viewManager.CreateAndBindViewForModelIfNecessary(viewmodel);

      var result = await DialogHost.Show(view, "RootDialogHost");
    }

    public void CopyStreamId(string streamId)
    {
      Clipboard.SetText(streamId);
      _events.Publish(new ShowNotificationEvent() { Notification = "Stream ID copied to clipboard!" });
    }

    public void OpenStreamInWeb(StreamState state)
    {
      Tracker.TrackPageview("stream", "web");
      Link.OpenInBrowser($"{state.ServerUrl}/streams/{state.Stream.id}");
    }

    public void Handle(StreamAddedEvent message)
    {
      StreamList.Insert(0, message.NewStream);
    }

    public void Handle(StreamUpdatedEvent message)
    {
      StreamList.Refresh();
    }

    public void Handle(StreamRemovedEvent message)
    {
      var state = StreamList.First(s => s.Stream.id == message.StreamId);
      StreamList.Remove(state);
    }

    public void Handle(ApplicationEvent message)
    {
      switch (message.Type)
      {
        case ApplicationEvent.EventType.DocumentClosed:
          {
            StreamList.Clear();
            break;
          }
        case ApplicationEvent.EventType.DocumentModified:
          {
            // warn that stream data may be expired
            break;
          }
        case ApplicationEvent.EventType.DocumentOpened:
        case ApplicationEvent.EventType.ViewActivated:
          StreamList.Clear();
          StreamList = new BindableCollection<StreamState>(message.DynamicInfo);
          break;
        case ApplicationEvent.EventType.ApplicationIdling:
          break;
        default:
          return;
      }
    }

    public void Handle(ReloadRequestedEvent message)
    {
      StreamList = LoadStreams();
    }
  }
}
