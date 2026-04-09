namespace AutoTerraMod.Common.World;

public interface ICorridor
{
	int Y { get; }

	int End(int direction);

	int Frontier(int direction);

	bool IsBuilt(int x);

	bool IsDeck(int x);

	bool WantsPlatform(int x);

	bool RequiresPlatform(int x);
}
