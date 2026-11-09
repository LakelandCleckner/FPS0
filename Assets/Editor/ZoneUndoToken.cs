using UnityEngine;

// Undo stand-in for zone mask edits: only this step counter is recorded, never the mask itself.
public class ZoneUndoToken : ScriptableObject
{
    public int step;
}
