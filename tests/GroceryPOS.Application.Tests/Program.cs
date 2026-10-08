// Entry point for the dependency-free test harness (see tests/GroceryPOS.Testing). Remove when moving to xUnit.
return GroceryPOS.Testing.TestRunner.Run(typeof(GroceryPOS.Application.Tests.Fakes.FakeClock).Assembly, args);
