namespace ASPAssistant.Tests.Services;

// 更新测试会短暂修改进程环境变量，不能与其他测试并行。
[CollectionDefinition("Update environment", DisableParallelization = true)]
public class UpdateEnvironmentCollection;
