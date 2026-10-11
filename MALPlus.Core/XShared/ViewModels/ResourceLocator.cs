using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using MALPlus.Adapters;
using MALPlus.Adapters.Credentials;
using MALPlus.Models.Interfaces;
using MALPlus.XShared.BL;
using MALPlus.XShared.Interfaces;
using IHandyDataStorage = MALPlus.XShared.ViewModels.Interfaces.IHandyDataStorage;

namespace MALPlus.XShared.ViewModels
{
    public static class ResourceLocator
    {
        public static IApplicationDataService ApplicationDataService
            => SimpleIoc.Default.GetInstance<IApplicationDataService>();

        public static IPasswordVault PasswordVaultProvider => SimpleIoc.Default.GetInstance<IPasswordVault>();

        public static IDataCache DataCacheService => SimpleIoc.Default.GetInstance<IDataCache>();

        public static IMessageDialogProvider MessageDialogProvider => SimpleIoc.Default.GetInstance<IMessageDialogProvider>();

        public static IClipboardProvider ClipboardProvider => SimpleIoc.Default.GetInstance<IClipboardProvider>();

        public static ISystemControlsLauncherService SystemControlsLauncherService  => SimpleIoc.Default.GetInstance<ISystemControlsLauncherService>();

        public static ILiveTilesManager LiveTilesManager  => SimpleIoc.Default.GetInstance<ILiveTilesManager>();

        public static IImageDownloaderService ImageDownloaderService  => SimpleIoc.Default.GetInstance<IImageDownloaderService>();

        public static ITelemetryProvider TelemetryProvider  => SimpleIoc.Default.GetInstance<ITelemetryProvider>();

        public static INotificationsTaskManager  NotificationsTaskManager  => SimpleIoc.Default.GetInstance<INotificationsTaskManager>();

        public static IChangeLogProvider  ChangelogProvider  => SimpleIoc.Default.GetInstance<IChangeLogProvider>();

        public static IHandyDataStorage HandyDataStorage => SimpleIoc.Default.GetInstance<IHandyDataStorage>();

        public static ISchdeuledJobsManger SchdeuledJobsManger => SimpleIoc.Default.GetInstance<ISchdeuledJobsManger>();

        public static ICssManager CssManager => SimpleIoc.Default.GetInstance<ICssManager>();

        public static IMalHttpContextProvider MalHttpContextProvider => SimpleIoc.Default.GetInstance<IMalHttpContextProvider>();

        public static ISnackbarProvider SnackbarProvider => SimpleIoc.Default.GetInstance<ISnackbarProvider>();

        public static IConnectionInfoProvider ConnectionInfoProvider => SimpleIoc.Default.GetInstance<IConnectionInfoProvider>();

        public static IDispatcherAdapter DispatcherAdapter => SimpleIoc.Default.GetInstance<IDispatcherAdapter>();

        public static IAiringInfoProvider AiringInfoProvider => SimpleIoc.Default.GetInstance<IAiringInfoProvider>();

        public static IDialogsProvider DialogsProvider => SimpleIoc.Default.GetInstance<IDialogsProvider>();

        public static IShareManager ShareManager => SimpleIoc.Default.GetInstance<IShareManager>();

        public static IEnglishTitlesProvider EnglishTitlesProvider => SimpleIoc.Default.GetInstance<IEnglishTitlesProvider>();

        /// <summary>
        ///     Null until a platform registers one, which is why every caller null-checks it: a
        ///     sign-out has to work on platforms that have no cookie jar at all.
        /// </summary>
        public static IMalWebSessionCleaner MalWebSessionCleaner
        {
            get
            {
                try
                {
                    return SimpleIoc.Default.GetInstance<IMalWebSessionCleaner>();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        // Purely shared interfaces

        public static IAnimeLibraryDataStorage AnimeLibraryDataStorage  => SimpleIoc.Default.GetInstance<IAnimeLibraryDataStorage>();

        public static void RegisterBase()
        {
            SimpleIoc.Default.Register<IAnimeLibraryDataStorage, AnimeLibraryDataStorage>();
            SimpleIoc.Default.Register<IHandyDataStorage, HandyDataStorage>();
            SimpleIoc.Default.Register<IAiringInfoProvider, AiringInfoProvider>();
            SimpleIoc.Default.Register<IShareManager, ShareManager>();
            SimpleIoc.Default.Register<IEnglishTitlesProvider, EnglishTitlesProvider>();
        }

        #region UsedByBackgroundTask

        public static void RegisterPasswordVaultAdapter(IPasswordVault vault)
        {
            SimpleIoc.Default.Register<IPasswordVault>(() => vault);
        }

        public static void RegisterAppDataServiceAdapter(IApplicationDataService appData)
        {
            SimpleIoc.Default.Register<IApplicationDataService>(() => appData);
        }

        public static void RegisterMessageDialogAdapter(IMessageDialogProvider msgDialog)
        {
            SimpleIoc.Default.Register<IMessageDialogProvider>(() => msgDialog);
        }

        public static void RegisterHttpContextAdapter(IMalHttpContextProvider httpContext)
        {
            SimpleIoc.Default.Register<IMalHttpContextProvider>(() => httpContext);
        }

        public static void RegisterDataCacheAdapter(IDataCache dataCache)
        {
            SimpleIoc.Default.Register<IDataCache>(() => dataCache);
        }

        /// <summary>
        ///     Destroys the MAL website session cookies on sign-out. Optional on purpose: the login
        ///     page asks for it lazily, because a platform with no cookie jar (or a Core unit test)
        ///     simply has nothing to clear and must not be forced to register a no-op.
        /// </summary>
        public static void RegisterMalWebSessionCleaner(IMalWebSessionCleaner cleaner)
        {
            SimpleIoc.Default.Register<IMalWebSessionCleaner>(() => cleaner);
        }
        #endregion

    }
}
