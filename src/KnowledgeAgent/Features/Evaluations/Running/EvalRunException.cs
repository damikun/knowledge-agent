namespace KnowledgeAgent.Features.Evaluations.Running;

public sealed class EvalRunException(string message, Exception? innerException = null) : Exception(message, innerException);
