// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
class Program
{
    static async Task Main(string[] args)
    {
        var nexar = new Nexar.Nexar();

        var headers = new Dictionary<string, string>
        {
            { "Accept", "application/json" },
        };

        try
        {
            var response = await nexar.GetAsync("https://freetestapi.com/api/v1/destinations/1", headers);
            Console.WriteLine(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
        finally
        {
            nexar.Dispose();
        }
    }
}
