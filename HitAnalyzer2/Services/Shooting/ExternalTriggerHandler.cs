using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class ExternalTriggerHandler
    {
        private bool _isPressed;

        internal ExternalTriggerHandler(Window window)
        {
            window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
            window.Deactivated += (_, _) => SetTriggerState(false);
        }

        internal event Action<bool, long>? PressChanged;

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.T)
                SetTriggerState(true);
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.T)
                SetTriggerState(false);
        }

        private void SetTriggerState(bool isPressed)
        {
            if (_isPressed == isPressed)
                return;

            _isPressed = isPressed;
            PressChanged?.Invoke(isPressed, Stopwatch.GetTimestamp());
        }
    }
}
