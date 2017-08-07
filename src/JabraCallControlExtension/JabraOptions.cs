using Genesyslab.Desktop.Infrastructure.Configuration;

namespace JabraCallControlExtension
{
  public class JabraOptions : Options
  {
    // Privileges (Genesys Roles & Privileges)
    // Define the names of the privileges which can be checked from the module
    // It can be Workspace Desktop Edition existing privileges or custom ones
    // Privileges can be enabled for an Agent/Person or for an entire Access Group
    private const string Privilege_CanUse_Str = "JabraCallControlExtension.canUse";
    private const string Privilege_CanUseGenesysSIPEndpoint_Str = "InteractionWorkspace.SIP.UseSIPEndpoint";

    #region Singleton

    public static JabraOptions Default { get; private set; }

    private JabraOptions(IConfigManager configManager) : base(configManager) { }

    /// <summary>
    /// Creates the singleton instance.
    /// </summary>
    /// <param name="theConfigManager">The config manager.</param>
    public static void CreateInstance(IConfigManager theConfigManager)
    {
      Default = new JabraOptions(theConfigManager);
    }

    #endregion

    #region Access Privileges

    // Check if the current Workspace Desktop Edition user has been assigned the JabraCallControlExtension CanUse privilege
    public bool CanUse()
    {
      // TODO - TO CHANGE - Verification of JabraCallControlExtension privilege disabled (for testing) - Always allow
      return true;

      if (this.Task[Privilege_CanUse_Str])
        return true;
      else
        return false;
    }

    // Check if the current Workspace Desktop Edition user has been assigned the Genesys SIPEndpoint privilege
    // The Agent could also be using a 3rd party SIP Phone (softphone or hardphone running aside, managed via 3rd party call control through Genesys SIP Server)
    public bool IsUsingGenesysSIPEndpoint()
    {
      if (this.Task[Privilege_CanUseGenesysSIPEndpoint_Str])
        return true;
      else
        return false;
    }

    #endregion
  }
}

