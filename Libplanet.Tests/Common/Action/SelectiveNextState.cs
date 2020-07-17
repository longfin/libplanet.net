using System;
using Bencodex.Types;
using Libplanet.Action;

namespace Libplanet.Tests.Common.Action
{
    public class SelectiveNextState : IAction
    {
        public IValue PlainValue =>
            Dictionary.Empty.Add((Text)"selected_miner", SelectedMiner.ToByteArray());

        public Address SelectedMiner { get; set; }

        public IAccountStateDelta Execute(IActionContext context)
        {
            if (context.Miner == SelectedMiner)
            {
                return context.PreviousStates
                    .SetState(context.Signer, (Text)"I LIKE IT")
                    .SetState(SelectedMiner, (Text)"ME TOO");
            }

            return context.PreviousStates
                .SetState(context.Signer, (Text)"I DON'T LIKE IT");
        }

        public void LoadPlainValue(IValue plainValue)
        {
            SelectedMiner = new Address((Binary)((Dictionary)plainValue)["selected_miner"]);
        }

        public void Render(IActionContext context, IAccountStateDelta nextStates)
        {
        }

        public void RenderError(IActionContext context, Exception exception)
        {
        }

        public void Unrender(IActionContext context, IAccountStateDelta nextStates)
        {
        }

        public void UnrenderError(IActionContext context, Exception exception)
        {
        }
    }
}
