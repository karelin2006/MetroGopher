using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.IsolatedStorage;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using MetroGopher.Resources;

namespace MetroGopher
{
    public partial class App : Application
    {
        /// <summary>
        /// Provides easy access to the root frame of the Phone Application.
        /// </summary>
        /// <returns>The root frame of the Phone Application.</returns>
        public static PhoneApplicationFrame RootFrame { get; private set; }

        /// <summary>
        /// Constructor for the Application object.
        /// </summary>
        public App()
        {
            // Global handler for uncaught exceptions.
            UnhandledException += Application_UnhandledException;

            // Standard XAML initialization
            InitializeComponent();

            // Phone-specific initialization
            InitializePhoneApplication();

            // Language display initialization
            InitializeLanguage();

            // Show graphics profiling information while debugging.
            if (Debugger.IsAttached)
            {
                Application.Current.Host.Settings.EnableFrameRateCounter = false;
                PhoneApplicationService.Current.UserIdleDetectionMode = IdleDetectionMode.Disabled;
            }
        }

        private void Application_ContractActivated(object sender, Windows.ApplicationModel.Activation.IActivatedEventArgs e)
        {
        }

        private void Application_Launching(object sender, LaunchingEventArgs e)
        {
        }

        private void Application_Activated(object sender, ActivatedEventArgs e)
        {
        }

        private void Application_Deactivated(object sender, DeactivatedEventArgs e)
        {
        }

        private void Application_Closing(object sender, ClosingEventArgs e)
        {
        }

        private void RootFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
        }

        private void Application_UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            if (Debugger.IsAttached)
            {
                Debug.WriteLine("--- UNHANDLED EXCEPTION ---");
                Debug.WriteLine(e.ExceptionObject.Message);
                Debug.WriteLine(e.ExceptionObject.StackTrace);
                if (e.ExceptionObject.InnerException != null)
                {
                    Debug.WriteLine("Inner: " + e.ExceptionObject.InnerException.Message);
                }
                Debugger.Break();
            }
        }

        #region Phone application initialization

        private bool phoneApplicationInitialized = false;

        private void InitializePhoneApplication()
        {
            if (phoneApplicationInitialized)
                return;

            RootFrame = new PhoneApplicationFrame();
            RootFrame.Navigated += CompleteInitializePhoneApplication;
            RootFrame.NavigationFailed += RootFrame_NavigationFailed;
            RootFrame.Navigated += CheckForResetNavigation;
            PhoneApplicationService.Current.ContractActivated += Application_ContractActivated;

            phoneApplicationInitialized = true;
        }

        private void CompleteInitializePhoneApplication(object sender, NavigationEventArgs e)
        {
            if (RootVisual != RootFrame)
                RootVisual = RootFrame;

            RootFrame.Navigated -= CompleteInitializePhoneApplication;
        }

        private void CheckForResetNavigation(object sender, NavigationEventArgs e)
        {
            if (e.NavigationMode == NavigationMode.Reset)
                RootFrame.Navigated += ClearBackStackAfterReset;
        }

        private void ClearBackStackAfterReset(object sender, NavigationEventArgs e)
        {
            RootFrame.Navigated -= ClearBackStackAfterReset;

            if (e.NavigationMode != NavigationMode.New && e.NavigationMode != NavigationMode.Refresh)
                return;

            while (RootFrame.RemoveBackEntry() != null)
            {
                ;
            }
        }

        #endregion

        private void InitializeLanguage()
        {
            try
            {
                string targetLang = CultureInfo.CurrentUICulture.Name;

                if (IsolatedStorageSettings.ApplicationSettings.Contains("AppLanguage"))
                {
                    var saved = IsolatedStorageSettings.ApplicationSettings["AppLanguage"] as string;
                    if (!string.IsNullOrEmpty(saved))
                    {
                        targetLang = saved;
                    }
                }

                var culture = new CultureInfo(targetLang);
                AppResources.Culture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;

                RootFrame.Language = XmlLanguage.GetLanguage(culture.Name);

                if (AppResources.ResourceFlowDirection == "RightToLeft")
                {
                    RootFrame.FlowDirection = FlowDirection.RightToLeft;
                }
                else
                {
                    RootFrame.FlowDirection = FlowDirection.LeftToRight;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Language init error: " + ex.Message);

                var fallbackCulture = new CultureInfo("ru-RU");
                AppResources.Culture = fallbackCulture;
                Thread.CurrentThread.CurrentCulture = fallbackCulture;
                Thread.CurrentThread.CurrentUICulture = fallbackCulture;
                RootFrame.Language = XmlLanguage.GetLanguage("ru-RU");
                RootFrame.FlowDirection = FlowDirection.LeftToRight;
            }
        }
    }
}