if (args is ["--version"])
{
    Console.WriteLine("pwdmgr agent cli 0.1.0");
    return 0;
}

Console.WriteLine("Privora Agent CLI bootstrap");
Console.WriteLine("Commands planned: login, unlock, secret get, agent status");
return 0;

