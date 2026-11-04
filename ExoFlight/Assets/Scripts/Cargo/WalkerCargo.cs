namespace ExoFlight.Cargo
{
    /// <summary>Walker placeholder. Derive specialised walkers here; other loads use TransportableCargo.</summary>
    public class WalkerCargo : TransportableCargo
    {
        public bool IsDeployed { get; private set; }

        protected override void OnDelivered() => IsDeployed = true;
        protected override void OnReset() => IsDeployed = false;
    }
}
