// 🟨 JavaScript/TypeScript client SDK

class WorkflowClient {
    constructor(
        private baseUrl: string,
        private apiKey?: string
    ) {}
    
    /**
     * Execute a workflow ✨
     */
    async executeWorkflow(
        workflowName: string,
        inputs?: Record<string, any>
    ): Promise<ExecutionResponse> {
        const response = await fetch(
            `${this.baseUrl}/api/v1/workflows/execute/${workflowName}`,
            {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(this.apiKey && { 'X-API-Key': this.apiKey })
                },
                body: JSON.stringify({ inputs })
            }
        );
        
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    }
    
    /**
     * Execute and wait for completion ⏳
     */
    async executeWorkflowSync(
        workflowName: string,
        inputs?: Record<string, any>,
        timeoutSeconds: number = 300
    ): Promise<ExecutionResponse> {
        const response = await fetch(
            `${this.baseUrl}/api/v1/workflows/execute/${workflowName}`,
            {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(this.apiKey && { 'X-API-Key': this.apiKey })
                },
                body: JSON.stringify({
                    inputs,
                    waitForCompletion: true,
                    timeoutSeconds
                })
            }
        );
        
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    }
    
    /**
     * Get execution status 📊
     */
    async getExecutionStatus(executionId: string): Promise<ExecutionStatusDetail> {
        const response = await fetch(
            `${this.baseUrl}/api/v1/workflows/executions/${executionId}`,
            {
                headers: {
                    ...(this.apiKey && { 'X-API-Key': this.apiKey })
                }
            }
        );
        
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    }
    
    /**
     * Connect to real-time updates 📡
     */
    connectToRealtime(): WorkflowHubConnection {
        return new WorkflowHubConnection(this.baseUrl, this.apiKey);
    }
}

// SignalR connection for real-time updates
class WorkflowHubConnection {
    private connection: signalR.HubConnection;
    
    constructor(baseUrl: string, apiKey?: string) {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(`${baseUrl}/hubs/workflow`, {
                accessTokenFactory: () => apiKey || ''
            })
            .withAutomaticReconnect()
            .build();
    }
    
    async start() {
        await this.connection.start();
    }
    
    async subscribeToExecution(executionId: string, callbacks: {
        onNodeStarted?: (event: NodeStartedEvent) => void;
        onNodeCompleted?: (event: NodeCompletedEvent) => void;
        onExecutionCompleted?: (event: ExecutionCompletedEvent) => void;
    }) {
        await this.connection.invoke('SubscribeToExecution', executionId);
        
        if (callbacks.onNodeStarted)
            this.connection.on('NodeStarted', callbacks.onNodeStarted);
        if (callbacks.onNodeCompleted)
            this.connection.on('NodeCompleted', callbacks.onNodeCompleted);
        if (callbacks.onExecutionCompleted)
            this.connection.on('ExecutionCompleted', callbacks.onExecutionCompleted);
    }
}

// Usage:
// const client = new WorkflowClient('https://workflow-engine.example.com', 'your-api-key');
// 
// // Execute workflow
// const execution = await client.executeWorkflow('data-processing', {
//     source: 'api',
//     format: 'json'
// });
// 
// console.log(`Started execution: ${execution.executionId}`);
// 
// // Real-time monitoring
// const hub = client.connectToRealtime();
// await hub.start();
// await hub.subscribeToExecution(execution.executionId, {
//     onNodeStarted: (event) => console.log(`Node started: ${event.nodeName}`),
//     onNodeCompleted: (event) => console.log(`Node completed: ${event.nodeName}`),
//     onExecutionCompleted: (event) => console.log(`Execution completed! Status: ${event.status}`)
// });

