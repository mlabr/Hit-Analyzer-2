using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class ExternalTriggerKeyboard
    {
        private bool _isPressed;

        internal ExternalTriggerKeyboard(Window window)
        {
            window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
            window.Deactivated += (_, _) => SetPressed(false);
        }

        internal event Action<bool, long>? PressChanged;

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.T)
                SetPressed(true);
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.T)
                SetPressed(false);
        }

        private void SetPressed(bool isPressed)
        {
            if (_isPressed == isPressed)
                return;

            _isPressed = isPressed;
            PressChanged?.Invoke(isPressed, Stopwatch.GetTimestamp());
        }
    }
}
