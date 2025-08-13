using System;
using System.Threading.Tasks;

namespace Vec3.Site.Generator.Templates;

public abstract class Content
{
	public virtual Func<Task<Content>>? GetSection(string name) => null;

	protected virtual void BeforeExecuteCore() { }
	protected abstract Task ExecuteCore();
	protected virtual void AfterExecuteCore(Exception? fault) { }

	private async Task DoExecute()
	{
		BeforeExecuteCore();

		try
		{
			await ExecuteCore();
		}
		catch (Exception fault)
		{
			AfterExecuteCore(fault);
			throw;
		}

		AfterExecuteCore(null);
	}

	private Task? execTask;
	public Task Execute()
	{
		return execTask ??= DoExecute();
	}

	protected void ThrowIfNotExecutedSuccessfully()
	{
		if (execTask == null || !execTask.IsCompleted)
			throw new InvalidOperationException();
		if (execTask.IsFaulted || execTask.IsCanceled)
			execTask.GetAwaiter().GetResult(); //rethrows for us
	}

	protected abstract string GetOutputCore();
	public string GetOutput()
	{
		ThrowIfNotExecutedSuccessfully();
		return GetOutputCore();
	}
}
