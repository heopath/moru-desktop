using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Moru;

// Short, 12 fps retained-mode bursts. No CompositionTarget.Rendering or permanent render loop.
internal sealed class PetAnimator : IDisposable
{
    private readonly FrameworkElement target;
    private readonly RotateTransform rotation = new();
    private readonly TranslateTransform translation = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly DispatcherTimer rest;
    private PetActivity state;
    private bool enabled = true, paused;
    public PetAnimator(FrameworkElement target)
    {
        this.target = target;
        target.RenderTransformOrigin = new Point(.5, .85);
        var transforms = new TransformGroup();
        transforms.Children.Add(scale); transforms.Children.Add(rotation); transforms.Children.Add(translation);
        target.RenderTransform = transforms;
        target.CacheMode = new BitmapCache();
        rest = new DispatcherTimer(DispatcherPriority.Background, target.Dispatcher);
        rest.Tick += (_, _) => { rest.Stop(); Pulse(); Schedule(); };
        target.IsVisibleChanged += (_, _) => Refresh();
        target.Loaded += (_, _) => Refresh();
    }
    public void SetState(PetActivity value)
    {
        if (state == value) return;
        state = value; Refresh();
    }
    public void SetEnabled(bool value) { enabled = value; Refresh(); }
    public void SetPaused(bool value) { if (paused == value) return; paused = value; Refresh(); }
    private bool CanMove => enabled && !paused && target.IsVisible && SystemParameters.ClientAreaAnimation;
    private void Refresh()
    {
        rest.Stop(); Clear();
        if (!CanMove) return;
        Pulse(); Schedule();
    }
    private void Schedule()
    {
        if (!CanMove || state is PetActivity.Done or PetActivity.Error) return;
        rest.Interval = TimeSpan.FromSeconds(state == PetActivity.Working ? 4 : 8);
        rest.Start();
    }
    public void Preview(PetActivity activity)
    {
        if (!CanMove) return;
        rest.Stop();
        var current = state;
        state = activity; Pulse(); state = current;
        Schedule();
    }
    private void Pulse()
    {
        if (!CanMove) return;
        Clear();
        switch (state)
        {
            case PetActivity.Working:
                Animate(rotation, RotateTransform.AngleProperty, 1.6, 0, -7, 7, -7, 7, 0);
                Animate(translation, TranslateTransform.YProperty, 1.6, 0, -3, 0, -3, 0, 0);
                break;
            case PetActivity.Done:
                Animate(translation, TranslateTransform.YProperty, 1.7, 0, -15, 0, -10, 0, 0);
                Animate(rotation, RotateTransform.AngleProperty, 1.7, 0, -8, 8, -5, 0, 0);
                break;
            case PetActivity.Waiting:
                Animate(rotation, RotateTransform.AngleProperty, 1.5, 0, -10, -10, 0);
                break;
            case PetActivity.Error:
                Animate(translation, TranslateTransform.XProperty, .7, 0, -4, 4, -4, 4, 0);
                break;
            default:
                Animate(translation, TranslateTransform.YProperty, 1.8, 0, -3, -4, 0);
                Animate(scale, ScaleTransform.ScaleYProperty, 1.8, 1, 1.025, 1.025, 1);
                break;
        }
    }
    private static void Animate(Animatable target, DependencyProperty property, double seconds, params double[] values)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, 12);
        for (int i = 0; i < values.Length; i++)
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * i / (values.Length - 1)))));
        animation.Freeze();
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
    private void Clear()
    {
        rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.BeginAnimation(TranslateTransform.YProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }
    public void Dispose() { rest.Stop(); Clear(); }
}
