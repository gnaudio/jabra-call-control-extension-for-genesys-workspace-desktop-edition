using Genesyslab.Desktop.Infrastructure;
using Genesyslab.Desktop.Infrastructure.Commands;
using Genesyslab.Desktop.Infrastructure.Configuration;
using Genesyslab.Desktop.Infrastructure.Events;
using Genesyslab.Desktop.Infrastructure.ViewManager;
using Genesyslab.Desktop.Modules.Core.Model.Agents;
using Genesyslab.Desktop.Modules.Core.Model.Interactions;
using Genesyslab.Desktop.Modules.Windows.Event;
using Genesyslab.Platform.Commons.Logging;
using Microsoft.Practices.Composite.Events;
using Microsoft.Practices.Composite.Wpf.Events;
using Microsoft.Practices.Unity;

namespace JabraCallControlExtension
{
  public class JabraModule : IModule
  {
    #region Private Members

    private readonly IUnityContainer container;
    private readonly IViewManager viewManager;
    private readonly IViewEventManager viewEventManager;
    private readonly ICommandManager commandManager;
    private readonly IConfigManager configManager;
    private readonly IInteractionManager interactionManager;
    private readonly IEventAggregator eventAggregator;
    private readonly IAgent agent;
    private readonly ILogger log;

    private const string moduleName = "JabraModule";

    private SubscriptionToken subscriptionTokenLoginEvent;
    private SubscriptionToken subscriptionTokenLogoutEvent;

    #endregion

    public JabraModule(IUnityContainer container,
      IViewManager viewManager,
      IViewEventManager viewEventManager,
      ICommandManager commandManager,
      IConfigManager configManager,
      IInteractionManager interactionManager,
      IEventAggregator eventAggregator,
      IAgent agent,
      ILogger log)
    {
      this.container = container;
      this.viewManager = viewManager;
      this.viewEventManager = viewEventManager;
      this.commandManager = commandManager;
      this.configManager = configManager;
      this.interactionManager = interactionManager;
      this.eventAggregator = eventAggregator;
      this.agent = agent;

      this.log = log.CreateChildLogger(moduleName);
      if (log.IsDebugEnabled)
        this.log.Debug("Constructor");
    }

    public void Initialize()
    {
      if (log.IsDebugEnabled)
        log.Debug("Initialize");

      // Initiliaze Module Options and Utils Singletons
      JabraOptions.CreateInstance(configManager);
      JabraUtils.CreateInstance(container, commandManager, interactionManager, agent, log);

      // Subscribe to LoginPreEvent and LogoutPreEvent
      // LoginPreEvent:
      // - will be triggered when the Workspace Desktop Edition native modules have finished their initialization
      // - useful to wait for this event when commands have to be inserted in WDE native chain of commands, or when subscribe on some native event handlers
      // LogoutPreEvent:
      // - will be triggered before the Workspace Desktop Edition application is exited
      subscriptionTokenLoginEvent = eventAggregator.GetEvent<LoginPreEvent>().Subscribe(LoginEventHandler, ThreadOption.PublisherThread, true);
      subscriptionTokenLogoutEvent = eventAggregator.GetEvent<LogoutPreEvent>().Subscribe(LogoutEventHandler, ThreadOption.PublisherThread, true);

      this.InitializeOnStart();
    }

    #region EventHandlers

    void LoginEventHandler(object o)
    {
      if (log.IsDebugEnabled)
        log.Debug("LoginEventHandler");

      this.InitializeOnLogin();
    }

    void LogoutEventHandler(object o)
    {
      if (log.IsDebugEnabled)
        log.Debug("LogoutEventHandler");

      eventAggregator.GetEvent<LoginPreEvent>().Unsubscribe(subscriptionTokenLoginEvent);
      eventAggregator.GetEvent<LogoutPreEvent>().Unsubscribe(subscriptionTokenLogoutEvent);

      this.TerminateOnLogout();
    }

    #endregion

    #region Helpers

    private void InitializeOnStart()
    {
      if (log.IsDebugEnabled)
        log.Debug("Initialize JabraModule - On Start");

      if (JabraOptions.Default.CanUse())
      {
        // RegisterViewsAndServices

        // RegisterViewsWithRegions

      }
    }

    private void InitializeOnLogin()
    {
      if (log.IsDebugEnabled)
        log.Debug("Initialize JabraModule - On Login");

      if (JabraOptions.Default.CanUse())
      {
        JabraUtils.Default.RegisterInteractionEventHandler();
      }
    }

    private void TerminateOnLogout()
    {
      if (log.IsDebugEnabled)
        log.Debug("Terminate JabraModule - On Logout");

      if (JabraOptions.Default.CanUse())
      {
        JabraUtils.Default.UnregisterInteractionEventHandler();
      }
    }

    #endregion

  }
}
