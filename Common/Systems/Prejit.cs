using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class Prejit : ModSystem
{
	const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	public override void PostSetupContent()
	{
		var assembly = Mod.Code;
		var logger = Mod.Logger;
		Task.Run(() =>
		{
			var clock = Stopwatch.StartNew();
			int compiled = 0;
			foreach (var type in assembly.GetTypes())
			{
				if (type.ContainsGenericParameters)
					continue;
				foreach (var method in type.GetMethods(Declared))
					compiled += Compile(method) ? 1 : 0;
				foreach (var constructor in type.GetConstructors(Declared))
					compiled += !constructor.IsStatic && Compile(constructor) ? 1 : 0;
			}
			logger.Info($"pre-compiled {compiled} methods in {clock.ElapsedMilliseconds}ms");
		});
	}

	static bool Compile(MethodBase method)
	{
		if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null)
			return false;
		try
		{
			RuntimeHelpers.PrepareMethod(method.MethodHandle);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
