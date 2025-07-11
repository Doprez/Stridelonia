﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Avalonia;
using Stride.Core.Diagnostics;
using Stride.Games;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Threading;
using Stride.Graphics;

namespace Stridelonia
{
    internal static class AvaloniaManager
    {
        private static readonly EventWaitHandle _initedEvent;
        private static readonly EventWaitHandle _runEvent;

        private static Thread _avaloniaThread;
        private static bool _isInitialize;
        //private static bool _stop;

        public static StridePlatformOptions Options { get; }

        static AvaloniaManager()
        {
            _initedEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
            _runEvent = new EventWaitHandle(false, EventResetMode.ManualReset);

            Options = AvaloniaLocator.Current.GetService<StridePlatformOptions>() ?? GetOptions();
            AvaloniaLocator.CurrentMutable.BindToSelf(Options);
        }

        public static void Initialize(IGame game)
        {
            if (_isInitialize) return;

            if (game.GetType().Name.Contains("Editor"))
            {
                var logger = GlobalLogger.GetLogger("Stridelonia");
                logger.Info("Stridelonia is disabled in GameStudio");
                return;
            }
            if (Options == null) return;

            AvaloniaLocator.CurrentMutable.BindToSelf(game);

            var dispatcher = game.Services.GetService<StrideDispatcher>();
            if (dispatcher == null)
            {
                dispatcher = new StrideDispatcher(game.Services);
                game.Services.AddService(dispatcher);
                game.GameSystems.Add(dispatcher);
            }

            if (Application.Current == null) StartAvalonia();

            _isInitialize = true;
        }

        public static void Run()
        {
            if (Options.UseMultiThreading)
                _runEvent.Set();
            else
            {
                var lifetime = (ClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
                lifetime.Exit += ExitEvent;
                lifetime.Start(Environment.GetCommandLineArgs());
            }
        }

        private static void ExitEvent(object sender, ControlledApplicationLifetimeExitEventArgs args)
        {
            var game = AvaloniaLocator.Current.GetService<IGame>();
            if (game is GameBase gameBase) gameBase.Exit();
        }

        public static void Stop()
        {
            var lifetime = (ClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
            Dispatcher.UIThread.Post(() =>
            {
                lifetime.Shutdown();
            });
        }

        private static void StartAvalonia()
        {
            if (Options.ConfigureApp == null || Options.ApplicationType == null)
            {
                var logger = GlobalLogger.GetLogger("Stridelonia");
                logger.Debug("No Application in StridePlatformOptions");
                return;
            }

            if (Options.UseMultiThreading)
            {
                _avaloniaThread = new Thread(AvaloniaThread)
                {
                    Name = "Avalonia Thread"
                };
                _avaloniaThread.Start(Options);
                _initedEvent.WaitOne();
                _initedEvent.Dispose();
            }
            else
            {
                CreateApplication(Options);
            }
        }

        private static void AvaloniaThread(object parameter)
        {
            var options = (StridePlatformOptions)parameter;

            CreateApplication(options);

            _initedEvent.Set();
            _runEvent.WaitOne();

            var lifetime = (ClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
            lifetime.Exit += ExitEvent;
            lifetime.Start(Environment.GetCommandLineArgs());
        }

        private static AppBuilder CreateApplication(StridePlatformOptions options)
        {
            var builderType = typeof(AppBuilderBase<>).MakeGenericType(typeof(AppBuilder));
            var configureMethod = builderType.GetMethod(nameof(AppBuilder.Configure), BindingFlags.Public | BindingFlags.Static, null, Array.Empty<Type>(), null);
            var builder = (AppBuilder)configureMethod.MakeGenericMethod(options.ApplicationType).Invoke(null, Array.Empty<object>());

            builder
                .UseStride();

            switch (GraphicsDevice.Platform)
            {
                default:
                    builder.UseSkia();
                    break;
            }

            options.ConfigureApp?.Invoke(builder);

            var lifetime = new ClassicDesktopStyleApplicationLifetime
            {
                Args = Environment.GetCommandLineArgs(),
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            builder.SetupWithLifetime(lifetime);

            return builder;
        }

        private static StridePlatformOptions GetOptions()
        {
            try
            {
                var configuratorConstructor =
                    AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetCustomAttributes<AvaloniaConfiguratorAttribute>())
                        .Single().ConfiguratorType.GetTypeInfo().DeclaredConstructors
                        .Where(c => c.GetParameters().Length == 0 && !c.IsStatic).Single();

                var configuratorMethod = configuratorConstructor.DeclaringType.GetTypeInfo().DeclaredMethods
                    .Single(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(StridePlatformOptions));

                var instance = configuratorConstructor.Invoke(Array.Empty<object>());
                return (StridePlatformOptions)configuratorMethod.Invoke(instance, Array.Empty<object>());
            }
            catch (InvalidOperationException)
            {
                var logger = GlobalLogger.GetLogger("Stridelonia");
                logger.Debug("No Application configurator found");
                return null;
            }
        }
    }
}
