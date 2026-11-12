using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Island;

public sealed class SpringEase : EasingFunctionBase
{
	public double Damping { get; set; } = 0.8;

	public double Stiffness { get; set; } = 9.0;

	public SpringEase()
	{
		EasingMode = EasingMode.EaseIn;
	}

	protected override double EaseInCore(double t)
	{
		if (t >= 1.0)
		{
			return 1.0;
		}
		double damping = Damping;
		double stiffness = Stiffness;
		double num = stiffness * Math.Sqrt(1.0 - damping * damping);
		return 1.0 - Math.Exp((0.0 - damping) * stiffness * t) * (Math.Cos(num * t) + damping * stiffness / num * Math.Sin(num * t));
	}

	protected override Freezable CreateInstanceCore()
	{
		return new SpringEase
		{
			Damping = Damping,
			Stiffness = Stiffness
		};
	}
}
