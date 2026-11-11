namespace SpcStudio;

// How a job running behind a progress box says where it has got to: the stage it is in, how many of that stage's items are done out of how many, and how far into the item in hand it is.
public delegate void JobProgress(string stage, int doneCount, int totalCount, double itemFraction = 0);
