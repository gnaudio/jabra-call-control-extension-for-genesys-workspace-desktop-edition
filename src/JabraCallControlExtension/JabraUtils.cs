using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Genesyslab.Desktop.Infrastructure;
using Genesyslab.Desktop.Infrastructure.Commands;
using Genesyslab.Desktop.Modules.Core.Model.Agents;
using Genesyslab.Desktop.Modules.Core.Model.Interactions;
using Genesyslab.Desktop.Modules.SIPEndpointCommunication;
using Genesyslab.Desktop.Modules.Voice.Model.Interactions;
using Genesyslab.Platform.Commons.Logging;
using Genesyslab.Platform.Commons.Protocols;
using Genesyslab.Platform.Voice.Protocols.TServer.Events;
using JabraHidTelephonyApi;
using Microsoft.Practices.Unity;

namespace JabraCallControlExtension
{
  public class JabraUtils
  {
    #region Private Members

    private readonly IUnityContainer container;
    private readonly ICommandManager commandManager;
    private readonly IInteractionManager interactionManager;
    private readonly IAgent agent;
    private readonly ILogger log;

    Dictionary<SIPEndpoint, SIPEndpoint> startedSipEndpoints = new Dictionary<SIPEndpoint, SIPEndpoint>();

    private IInteractionVoice incomingCall = null;
    private IInteractionVoice activeCall = null;
    private List<IInteractionVoice> heldCalls = new List<IInteractionVoice>();
    private bool isCallMuted = false;

    #endregion

    #region Singleton

    public static JabraUtils Default { get; private set; }

    /// <summary>
    /// Creates the singleton instance.
    /// </summary>
    /// <param name="theConfigManager">The config manager.</param>
    public static void CreateInstance(IUnityContainer container,
      ICommandManager commandManager,
      IInteractionManager interactionManager,
      IAgent agent,
      ILogger log)
    {
      Default = new JabraUtils(container, commandManager, interactionManager, agent, log);
    }

    #endregion

    public JabraUtils(IUnityContainer container,
      ICommandManager commandManager,
      IInteractionManager interactionManager,
      IAgent agent,
      ILogger log)
    {
      this.container = container;
      this.commandManager = commandManager;
      // ICommandManager could also be retrieved/resolved via the container
      // this.commandManager = container.Resolve<ICommandManager>();
      this.interactionManager = interactionManager;
      // IInteractionManager could also be retrieved/resolved via the container
      // this.interactionManager = container.Resolve<IInteractionManager>();
      this.agent = agent;
      // IAgent could also be retrieved/resolved via the container
      // this.agent = container.Resolve<IAgent>();

      this.log = log;

      InitJabraLibrary();
    }

    #region Jabra device management

    private object jabraLock = new object();
    private IDeviceService jabraDeviceService;
    private List<IHidTelephonyDevice> jabraDevices = new List<IHidTelephonyDevice>();
    private BlockingCollection<Action> workQueue = new BlockingCollection<Action>();

    private void InitJabraLibrary()
    {
      // Get the work-queue up and running
      Task.Factory.StartNew(() =>
      {
        while (true)
        {
          var action = workQueue.Take();
          try
          {
            action();
          }
          catch (Exception exception)
          {
            // Don't do anyting - only log the exception
            log.Error("Got an workQueue exception", exception);
          }
        }
      });

      // Get a device service interface
      jabraDeviceService = ServiceFactory.CreateDeviceService();

      // Get a list of currently available Jabra devices
      ReadOnlyCollection<IHidTelephonyDevice> devices = jabraDeviceService.AvailableDevices;
      foreach (var device in devices)
      {
        AddJabraDevice(device);
      }

      // Subscribe to device added/removed events
      jabraDeviceService.DeviceAdded += OnJabraDeviceAdded;
      jabraDeviceService.DeviceRemoved += OnJabraDeviceRemoved;
    }

    private void OnJabraDeviceAdded(object sender, DeviceAddedEventArgs deviceAddedEventArgs)
    {
      Action action = () =>
      {
        AddJabraDevice(deviceAddedEventArgs.Device);
      };
      workQueue.Add(action);
    }

    private void OnJabraDeviceRemoved(object sender, DeviceRemovedEventArgs deviceRemovedEventArgs)
    {
      Action action = () =>
      {
        RemoveJabraDevice(deviceRemovedEventArgs.Device);
      };
      workQueue.Add(action);
    }

    private void AddJabraDevice(IHidTelephonyDevice device)
    {
      lock (jabraLock)
      {
        log.Info("Jabra device added");
        device.ButtonInput += OnButtonInput;
        jabraDevices.Add(device);
      }
    }

    private void RemoveJabraDevice(IHidTelephonyDevice device)
    {
      lock (jabraLock)
      {
        log.Info("Jabra device removed");
        device.ButtonInput -= OnButtonInput;
        jabraDevices.Remove(device);
      }
    }

    private void OnButtonInput(object sender, ButtonInputEventArgs e)
    {
      if (e.Value.HasValue)
      {
        log.Info($"OnButtonInput: {e.Button.ToString()}, {e.Value.Value}");
      }
      else
      {
        log.Info($"OnButtonInput: {e.Button.ToString()}, (no value)");
      }

      #region Mic mute

      if (e.Button == ButtonId.MicMute)
      {
        Action action = () =>
        {
          lock (jabraLock)
          {
            if (activeCall != null)
            {
              if (!isCallMuted)
              {
                RequestMuteCallViaSIPEndpoint(activeCall);
              }
              else
              {
                RequestUnmuteCallViaSIPEndpoint(activeCall);
              }
            }
          }
        };
        workQueue.Add(action);
      }

      #endregion

      #region Flash

      if (e.Button == ButtonId.Flash)
      {
        if (activeCall != null)
        {
          RequestHoldCall(activeCall);
        }
        else if (heldCalls.Any())
        {
          RequestRetrieveCall(heldCalls.First());
        }
      }

      #endregion

      #region Reject call

      if (e.Button == ButtonId.RejectCall)
      {
        Action action = () =>
        {
          lock (jabraLock)
          {
            if (incomingCall != null)
            {
              if (e.Value.HasValue && e.Value == true)
              {
                RequestReleaseCall(incomingCall);
              }
            }
          }

        };
        workQueue.Add(action);
      }

      #endregion

      #region Hook

      if (e.Button == ButtonId.HookSwitch)
      {
        Action action = () =>
        {
          lock (jabraLock)
          {
            if (incomingCall != null)
            {
              if (e.Value.HasValue && e.Value == true)
              {
                RequestAnswerCall(incomingCall);
              }
            }
            else if (activeCall != null)
            {
              if (e.Value.HasValue && e.Value == false)
              {
                RequestReleaseCall(activeCall);
              }
            }
          }
        };
        workQueue.Add(action);
      }

      #endregion
    }

    private void SetHookState(bool offHook)
    {
      lock (jabraLock)
      {
        log.Info($"SetHookState: {offHook}");

        foreach (var jabraDevice in jabraDevices)
        {
          if (!jabraDevice.IsLocked)
          {
            jabraDevice.Lock();
          }
          jabraDevice.SetHookState(offHook);
          if (!offHook)
          {
            jabraDevice.Unlock();
          }
        }
      }
    }

    private void SetMicrophoneMuted(bool muted)
    {
      lock (jabraLock)
      {
        log.Info($"SetMicrophoneMuted: {muted}");

        isCallMuted = muted;
        foreach (var jabraDevice in jabraDevices)
        {
          if (!jabraDevice.IsLocked)
          {
            jabraDevice.Lock();
          }
          jabraDevice.SetMicrophoneMuted(muted);
        }
      }
    }

    private void SetRinger(bool ringing, string callerId = null)
    {
      lock (jabraLock)
      {
        log.Info($"SetRinger: {ringing}");

        foreach (var jabraDevice in jabraDevices)
        {
          if (!jabraDevice.IsLocked)
          {
            jabraDevice.Lock();
          }
          if (string.IsNullOrEmpty(callerId))
          {
            jabraDevice.SetRinger(ringing);
          }
          else
          {
            jabraDevice.SetRinger(ringing, callerId);
          }
        }
      }
    }

    private void SetCallOnHold(bool onHold)
    {
      lock (jabraLock)
      {
        log.Info($"SetCallOnHold: {onHold}");

        foreach (var jabraDevice in jabraDevices)
        {
          if (!jabraDevice.IsLocked)
          {
            jabraDevice.Lock();
          }
          jabraDevice.SetCallOnHold(onHold);
        }
      }
    }

    #endregion

    #region API Requests Placeholder

    public IInteractionVoice FindVoiceInteraction(string token)
    {
      IInteractionVoice interactionVoice = null;

      foreach (var interaction in interactionManager.Interactions)
      {
        IInteractionVoice iv = interaction as IInteractionVoice;
        if (iv != null && iv.Media.IsSIPMedia)
        {
          // TODO
          // Decide how to leverage token to recognize interaction (as there can be multiple active calls)
          // token can leverage Interaction.Id, ConnectionId, ANI, ..., or a key/value pair in UserData
          interactionVoice = iv;
          break;
        }
      }
      return interactionVoice;
    }

    delegate void RequestAnswerCallDelegate(IInteractionVoice ixnVoice);

    public void RequestAnswerCall(IInteractionVoice ixnVoice)
    {
      // To go to the main thread
      if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
      {
        object result = Application.Current.Dispatcher.Invoke(DispatcherPriority.Send,
          new RequestAnswerCallDelegate(RequestAnswerCall), ixnVoice);
      }
      else
      {
        log.Info("Execute RequestAnswerCall");
        TriggerVoiceChainOfCommands(ixnVoice, "InteractionVoiceAnswerCall");
      }
    }

    public void RequestAnswerCall(string token)
    {
      var ixnVoice = this.FindVoiceInteraction(token);
      this.RequestAnswerCall(ixnVoice);
    }

    delegate void RequestReleaseCallDelegate(IInteractionVoice ixnVoice);

    public void RequestReleaseCall(IInteractionVoice ixnVoice)
    {
      // To go to the main thread
      if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
      {
        object result = Application.Current.Dispatcher.Invoke(DispatcherPriority.Send,
          new RequestReleaseCallDelegate(RequestReleaseCall), ixnVoice);
      }
      else
      {
        log.Info("Execute RequestReleaseCall");
        TriggerVoiceChainOfCommands(ixnVoice, "InteractionVoiceReleaseCall");
      }
    }

    public void RequestReleaseCall(string token)
    {
      var ixnVoice = this.FindVoiceInteraction(token);
      this.RequestReleaseCall(ixnVoice);
    }

    delegate void RequestHoldCallDelegate(IInteractionVoice ixnVoice);

    public void RequestHoldCall(IInteractionVoice ixnVoice)
    {
      // To go to the main thread
      if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
      {
        object result = Application.Current.Dispatcher.Invoke(DispatcherPriority.Send,
          new RequestHoldCallDelegate(RequestHoldCall), ixnVoice);
      }
      else
      {
        log.Info("Execute RequestHoldCall");
        TriggerVoiceChainOfCommands(ixnVoice, "InteractionVoiceHoldCall");
      }
    }

    public void RequestHoldCall(string token)
    {
      var ixnVoice = this.FindVoiceInteraction(token);
      this.RequestHoldCall(ixnVoice);
    }

    delegate void RequestRetrieveCallDelegate(IInteractionVoice ixnVoice);

    public void RequestRetrieveCall(IInteractionVoice ixnVoice)
    {
      // To go to the main thread
      if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
      {
        object result = Application.Current.Dispatcher.Invoke(DispatcherPriority.Send,
          new RequestRetrieveCallDelegate(RequestRetrieveCall), ixnVoice);
      }
      else
      {
        log.Info("Execute RequestRetrieveCall");
        TriggerVoiceChainOfCommands(ixnVoice, "InteractionVoiceRetrieveCall");
      }
    }

    public void RequestRetrieveCall(string token)
    {
      var ixnVoice = this.FindVoiceInteraction(token);
      this.RequestRetrieveCall(ixnVoice);
    }

    #endregion

    #region Headset Voice Chains of Commands

    private void TriggerVoiceChainOfCommands(IInteractionVoice ixnVoice, String commandName)
    {
      try
      {
        IDictionary<string, object> parameters = new Dictionary<string, object>();

        parameters.Add("CommandParameter", ixnVoice);
        // parameters.Add("OtherKey", "OtherValue");

        IChainOfCommand voiceCallCommand = commandManager.GetChainOfCommandByName(commandName);

        if (voiceCallCommand != null)
        {
          voiceCallCommand.Execute(parameters);

          // Async way
          /*
          voiceCallCommand.BeginExecute(parameters,
                  delegate(IAsyncResult ar)
                  {
                      voiceCallCommand.EndExecute(ar);
                  }, null);
           */
        }
      }
      catch (Exception exp)
      {
        log.Error("Exception in TriggerVoiceChainOfCommands", exp);
      }
    }

    #endregion

    #region Headset MakeCall Chain of Commands

    delegate void RequestMakeCallDelegate(String destination, String location);

    public void RequestMakeCall(String destination, String location)
    {
      // To go to the main thread
      if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
      {
        object result = Application.Current.Dispatcher.Invoke(DispatcherPriority.Send,
          new RequestMakeCallDelegate(RequestMakeCall), destination, location);
      }
      else
      {
        log.Info("Execute RequestMakeCall");
        try
        {
          IDictionary<string, object> parameters = new Dictionary<string, object>();

          parameters.Add("CommandParameter", agent.FirstMediaVoice);
          parameters.Add("Destination", destination);
          if (location == null)
            location = "";
          parameters.Add("Location", location);
          parameters.Add("MakeCallType", Genesyslab.Enterprise.Model.Interaction.MakeCallType.Regular);

          IChainOfCommand voiceCallCommand = commandManager.GetChainOfCommandByName("MediaVoiceMakeCall");

          if (voiceCallCommand != null)
          {
            voiceCallCommand.Execute(parameters);

            // Async way
            /*
            voiceCallCommand.BeginExecute(parameters,
                    delegate(IAsyncResult ar)
                    {
                        voiceCallCommand.EndExecute(ar);
                    }, null);
             */
          }
        }
        catch (Exception exp)
        {
          log.Error("Exception in RequestMakeCall", exp);
        }
      }
    }

    #endregion

    #region Notifications

    public void RegisterInteractionEventHandler()
    {
      interactionManager.InteractionEvent += InteractionEvent;
    }

    public void UnregisterInteractionEventHandler()
    {
      interactionManager.InteractionEvent -= InteractionEvent;
    }

    private void InteractionEvent(object sender, EventArgs<IInteraction> e)
    {
      IInteraction interaction = e.Value;
      IInteractionVoice iv = interaction as IInteractionVoice;
      if (iv != null && iv.Media.IsSIPMedia)
      {
        IMessage receivedEvent = iv.EntrepriseLastInteractionEvent;

        if (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.PresentedIn)
        {
          if (receivedEvent.Id == EventRinging.MessageId)
          {
            RegisterSIPEPEventHandlers(iv);

            /* Ringing */
            log.Info("InteractionEvent: Ringing");

            // Update Jabra device state
            SetRinger(true, iv.PhoneNumber);
            incomingCall = iv;
          }
        }
        else if (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.Connected)
        {
          if (receivedEvent.Id == EventEstablished.MessageId)
          {
            /* Established */
            log.Info("InteractionEvent: Established");

            // Update Jabra device state
            SetRinger(false);
            SetHookState(true);
            incomingCall =  null;
            activeCall = iv;

            // Always start unmuted
            isCallMuted = false;
            SetMicrophoneMuted(false);
          }
          else if (receivedEvent.Id == EventRetrieved.MessageId)
          {
            /* Retrieved */
            log.Info("InteractionEvent: Retrieved");

            // Update Jabra device state
            SetHookState(true);

            heldCalls.Remove(iv);
            if (heldCalls.Count == 0)
            {
              SetCallOnHold(false);
            }
            activeCall = iv;

            // Always start unmuted
            isCallMuted = false;
            SetMicrophoneMuted(false);
          }
        }
        else if (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.Held)
        {
          if (receivedEvent.Id == EventHeld.MessageId)
          {
            /* Held */
            log.Info("InteractionEvent: Held");

            // Update Jabra device state
            SetCallOnHold(true);
            SetHookState(false);
            activeCall = null;
            heldCalls.Add(iv);
          }
        }
        else if ((iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.Ended) ||
                 (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.Abandonned) ||
                 (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.Dropped) ||
                 (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.Redirected))
        {
          // Sent two times....

          /* Ended */
          log.Info("InteractionEvent: Ended");

          // Update Jabra device state
          SetRinger(false);
          if (iv == activeCall)
          {
            SetHookState(false);
            activeCall = null;
          }
          else
          {
            if (heldCalls.Contains(iv))
            {
              heldCalls.Remove(iv);
              if (heldCalls.Count == 0)
              {
                SetCallOnHold(false);
              }
            }
          }
        }
        else if (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.PresentedOut)
        {
          if (receivedEvent.Id == EventDialing.MessageId)
          {
            RegisterSIPEPEventHandlers(iv);

            /* Dialing */
            log.Info("InteractionEvent: Dialing");

          }
          else if (receivedEvent.Id == EventNetworkReached.MessageId)
          {

          }
        }
        else if (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.Busy)
        {

        }
        else if (iv.State == Genesyslab.Enterprise.Model.Interaction.InteractionStateType.InvalidDestination)
        {

        }
      }
    }

    #endregion

    #region Specific to mute/unmute

    delegate void RequestMuteCallViaSIPEndpointDelegate(IInteractionVoice ixnVoice);

    public void RequestMuteCallViaSIPEndpoint(IInteractionVoice ixnVoice)
    {
      // To go to the main thread
      if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
      {
        object result = Application.Current.Dispatcher.Invoke(DispatcherPriority.Send,
          new RequestMuteCallViaSIPEndpointDelegate(RequestMuteCallViaSIPEndpoint), ixnVoice);
      }
      else
      {
        log.Info("Execute RequestMuteCallViaSIPEndpoint");
        if (JabraOptions.Default.IsUsingGenesysSIPEndpoint())
        {
          try
          {
            if (ixnVoice != null)
            {
              SIPEndpoint sipEndpoint = container.Resolve<ISIPEndpointCommunication>().FindSIPEndpoint(ixnVoice);
              if (sipEndpoint != null)
              {
                //sipEndpoint.IsMicrophoneMuted = true;
              }
            }
          }
          catch (Exception exp)
          {
            log.Error("Exception in RequestMuteCallViaSIPEndpoint", exp);
          }
        }
      }

    }

    public void RequestMuteCallViaSIPEndpoint(string token)
    {
      IInteractionVoice interactionVoice = this.FindVoiceInteraction(token);

      if (interactionVoice != null)
      {
        RequestMuteCallViaSIPEndpoint(interactionVoice);
      }
    }

    delegate void RequestUnmuteCallViaSIPEndpointDelegate(IInteractionVoice ixnVoice);

    public void RequestUnmuteCallViaSIPEndpoint(IInteractionVoice ixnVoice)
    {
      // To go to the main thread
      if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
      {
        object result = Application.Current.Dispatcher.Invoke(DispatcherPriority.Send,
          new RequestUnmuteCallViaSIPEndpointDelegate(RequestUnmuteCallViaSIPEndpoint), ixnVoice);
      }
      else
      {
        log.Info("Execute RequestUnmuteCallViaSIPEndpoint");
        if (JabraOptions.Default.IsUsingGenesysSIPEndpoint())
        {
          try
          {
            if (ixnVoice != null)
            {
              SIPEndpoint sipEndpoint = container.Resolve<ISIPEndpointCommunication>().FindSIPEndpoint(ixnVoice);
              if (sipEndpoint != null)
              {
                //sipEndpoint.IsMicrophoneMuted = false;
              }
            }
          }
          catch (Exception exp)
          {
            log.Error("Exception in RequestMuteCallViaSIPEndpoint", exp);
          }
        }
      }
    }

    public void RequestUnmuteCallViaSIPEndpoint(string token)
    {
      IInteractionVoice interactionVoice = this.FindVoiceInteraction(token);

      if (interactionVoice != null)
      {
        RequestUnmuteCallViaSIPEndpoint(interactionVoice);
      }
    }

    private void RegisterSIPEPEventHandlers(IInteractionVoice interactionVoice)
    {
      if (JabraOptions.Default.IsUsingGenesysSIPEndpoint())
      {
        try
        {
          SIPEndpoint sipEndpoint = container.Resolve<ISIPEndpointCommunication>().FindSIPEndpoint(interactionVoice);

          if (sipEndpoint != null)
          {
            // this.sipEP.EndpointStatusChanged += new System.EventHandler<EventArgs<bool>>(SIPEP_StatusChanged);
            if (!startedSipEndpoints.ContainsKey(sipEndpoint))
            {
              startedSipEndpoints[sipEndpoint] = sipEndpoint;

              sipEndpoint.PropertyChanged += new PropertyChangedEventHandler(SIPEP_PropertyChanged);
              sipEndpoint.EndpointStatusChanged += new System.EventHandler<EventArgs<bool>>(SIPEP_StatusChanged);
            }
          }
        }
        catch (Exception exp)
        {
          log.Error("Exception in RegisterSIPEPEventHandlers", exp);
        }
      }
    }

    private void UnregisterSIPEPEventHandlers(IInteractionVoice interactionVoice)
    {
      if (JabraOptions.Default.IsUsingGenesysSIPEndpoint())
      {
        try
        {
          SIPEndpoint sipEndpoint = container.Resolve<ISIPEndpointCommunication>().FindSIPEndpoint(interactionVoice);

          if (sipEndpoint != null)
          {
            // this.sipEP.EndpointStatusChanged += new System.EventHandler<EventArgs<bool>>(SIPEP_StatusChanged);
            if (startedSipEndpoints.ContainsKey(sipEndpoint))
            {
              sipEndpoint.PropertyChanged -= new PropertyChangedEventHandler(SIPEP_PropertyChanged);
              sipEndpoint.EndpointStatusChanged -= new System.EventHandler<EventArgs<bool>>(SIPEP_StatusChanged);

              startedSipEndpoints.Remove(sipEndpoint);
            }
          }
        }
        catch (Exception exp)
        {
          log.Error("Exception in UnregisterSIPEPEventHandlers", exp);
        }
      }
    }

    private void SIPEP_StatusChanged(object sender, EventArgs<bool> e)
    {
      log.Debug("SIPEP StatusChangedEvent received");
    }

    private void SIPEP_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == "IsMicrophoneMuted")
      {
//        SIPEndpoint sipEndpoint = sender as SIPEndpoint;
//        bool microphoneMuted = sipEndpoint.IsMicrophoneMuted;
//
//        /* Mic mute change */
//
//        SetMicrophoneMuted(microphoneMuted);
      }
      // Should also work with IsSpeakerMuted, MicrophoneVolume, SpeakerVolume
    }

    #endregion

  }
}
