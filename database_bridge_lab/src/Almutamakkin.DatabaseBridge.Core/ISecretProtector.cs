namespace Almutamakkin.DatabaseBridge.Core;

public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
