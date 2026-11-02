namespace FishTank;

public sealed class GeneratorOptions
{
	public int Length = 24;

	public bool Lower = true;

	public bool Upper = true;

	public bool Digits = true;

	public bool Symbols = true;

	public bool ExcludeAmbiguous;

	public string SymbolCharacters = "!@#$%^&*()-_=+[]{}:,.?";
}
