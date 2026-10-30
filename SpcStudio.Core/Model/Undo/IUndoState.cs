namespace SpcStudio.Model;

// What an undo history needs of the states on its stacks: whether two of them hold the same document.
public interface IUndoState<TSelf> where TSelf : class, IUndoState<TSelf>
{
    // Whether the two hold the same document in every respect an edit can change.
    public bool HoldsSameStateAs(TSelf other);
}
