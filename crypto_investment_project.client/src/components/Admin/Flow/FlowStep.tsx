import {
    ArrowRightOutlined,
    BranchesOutlined,
    CheckCircleOutlined,
    ClockCircleOutlined,
    CloseCircleOutlined,
    ControlOutlined,
    ForkOutlined,
    LoadingOutlined,
    PauseCircleOutlined,
    RetweetOutlined,
    RightOutlined,
    WarningOutlined
} from '@ant-design/icons';
import {
    Badge,
    Button,
    Card,
    Descriptions,
    Space,
    Tag,
    Tooltip,
    Typography
} from 'antd';

import Modal from 'antd/es/modal/Modal';
import { JSX, useState } from 'react';
import type {
    StepDto,
    StepStatusKey,
    SubStepDto
} from '../../../services/flowService';

// Import the flow service and SignalR

const { Text } = Typography;

// Step Status Colors
const stepStatusConfig: Record<StepStatusKey, { color: string; icon: JSX.Element; label: string; bgColor: string; borderColor: string }> = {
    Pending: { color: '#d9d9d9', icon: <ClockCircleOutlined />, label: 'Pending', bgColor: '#f5f5f5', borderColor: '#d9d9d9' },
    InProgress: { color: '#1890ff', icon: <LoadingOutlined spin />, label: 'In Progress', bgColor: '#e6f7ff', borderColor: '#1890ff' },
    Completed: { color: '#52c41a', icon: <CheckCircleOutlined />, label: 'Completed', bgColor: '#f6ffed', borderColor: '#52c41a' },
    Failed: { color: '#ff4d4f', icon: <CloseCircleOutlined />, label: 'Failed', bgColor: '#fff1f0', borderColor: '#ff4d4f' },
    Skipped: { color: '#8c8c8c', icon: <RightOutlined />, label: 'Skipped', bgColor: '#fafafa', borderColor: '#d9d9d9' },
    Paused: { color: '#faad14', icon: <PauseCircleOutlined />, label: 'Paused', bgColor: '#fffbe6', borderColor: '#faad14' }
};

interface FlowStepProps {
    step: StepDto | SubStepDto;
    isCurrentStep: boolean;
    isBranchStep: boolean;
}

const FlowStep: React.FC<FlowStepProps> = ({
    step,
    isCurrentStep,
    isBranchStep = false,
}) => {
    const [stepDetailModal, setStepDetailModal] = useState(false);
    //const [loading, setLoading] = useState(false);

    const stepStatus = stepStatusConfig[step.status as StepStatusKey] || stepStatusConfig.Pending;

    const handleOnStepClick = () => {
        setStepDetailModal(true);
        console.log("FlowStep::handleOnStepClick => step: ", step);
    }

    // Render branches for a step
    const renderBranches = () => {
        if (!step.branches || step.branches.length === 0) return null;

        return (
            <div style={{
                marginTop: '30px',
                padding: '20px',
                background: 'linear-gradient(180deg, #f8f9fa 0%, #ffffff 100%)',
                borderRadius: '8px',
                border: '1px dashed #d9d9d9'
            }}>
                {/* Branch Header */}
                <div style={{ marginBottom: '16px', textAlign: 'center' }}>
                    <Tag color="purple" icon={<BranchesOutlined />}>
                        Branching Point
                    </Tag>
                </div>

                {/* Branches Container */}
                <div style={{
                    display: 'flex',
                    gap: '30px',
                    justifyContent: 'center',
                    alignItems: 'flex-start',
                    flexWrap: 'wrap'
                }}>
                    {step.branches.map((branch, branchIndex) => (
                        <div
                            key={branchIndex}
                            style={{
                                padding: '16px',
                                background: branch.isDefault ? '#f0f8ff' : '#fff',
                                borderRadius: '8px',
                                border: branch.isDefault ? '2px solid #1890ff' : '1px solid #e8e8e8',
                                position: 'relative'
                            }}
                        >
                            {/* Branch Header */}
                            <div style={{ marginBottom: '12px', textAlign: 'center' }}>
                                {branch.isDefault ? (
                                    <Tag color="blue">Default Branch</Tag>
                                ) : (
                                    <Tooltip title={branch.condition || 'Custom condition'}>
                                        <Tag color="orange">
                                            Conditional Branch
                                        </Tag>
                                    </Tooltip>
                                )}
                                {branch.condition && !branch.isDefault && (
                                    <div style={{ marginTop: '8px' }}>
                                        <Text type="secondary" style={{ fontSize: '12px' }}>
                                            Condition: {branch.condition}
                                        </Text>
                                    </div>
                                )}
                            </div>

                            {/* Branch Steps */}
                            {branch.steps && branch.steps.length > 0 ? (
                                <div style={{
                                    display: 'flex',
                                    flexDirection: 'column',
                                    gap: '16px',
                                    alignItems: 'center',
                                }}>
                                    {branch.steps.length > 0 && branch.steps.map((branchStep, stepIndex) => {
                                        return (
                                            <div key={branchStep.name}>
                                                {stepIndex > 0 && (
                                                    <ArrowRightOutlined
                                                        rotate={90}
                                                        style={{
                                                            fontSize: '18px',
                                                            color: '#d9d9d9',
                                                            marginBottom: '8px'
                                                        }}
                                                    />
                                                )}
                                                <FlowStep step={branchStep} isCurrentStep={isCurrentStep} isBranchStep={true} />
                                            </div>
                                        );
                                    })}
                                </div>
                            ) : (
                                <div style={{ textAlign: 'center', padding: '20px' }}>
                                    <Text type="secondary" style={{ fontSize: '12px' }}>
                                        No steps in this branch
                                    </Text>
                                </div>
                            )}
                        </div>
                    ))}
                </div>

                {/* Branch Merge Indicator */}
                <div style={{ textAlign: 'center', marginTop: '20px' }}>
                    <ArrowRightOutlined
                        rotate={90}
                        style={{ fontSize: '24px', color: '#d9d9d9' }}
                    />
                    <div style={{ marginTop: '8px' }}>
                        <Text type="secondary" style={{ fontSize: '12px' }}>
                            Branches merge here
                        </Text>
                    </div>
                </div>
            </div>
        );
    };

    return (
        <>
            <Card
                hoverable
                onClick={handleOnStepClick}
                style={{
                    width: isBranchStep ? '180px' : '200px',
                    border: `2px solid ${stepStatus.borderColor}`,
                    background: stepStatus.bgColor,
                    boxShadow: isCurrentStep ? '0 0 10px rgba(24, 144, 255, 0.5)' : undefined,
                    position: 'relative'
                }}
                bodyStyle={{ padding: isBranchStep ? '10px' : '12px' }}
            >
                {/* Current step indicator */}
                {isCurrentStep && (
                    <Badge
                        status="processing"
                        style={{
                            position: 'absolute',
                            top: '-8px',
                            right: '-8px',
                            zIndex: 1
                        }}
                    />
                )}

                {/* Step Header */}
                <div style={{ marginBottom: '8px' }}>
                    <Space align="center">
                        <span style={{ color: stepStatus.color, fontSize: isBranchStep ? '14px' : '16px' }}>
                            {stepStatus.icon}
                        </span>
                        <Text strong ellipsis style={{ maxWidth: isBranchStep ? '130px' : '150px', fontSize: isBranchStep ? '13px' : '14px' }}>
                            {step.name}
                        </Text>
                    </Space>
                </div>

                {/* Step Status */}
                <div style={{ marginBottom: '8px' }}>
                    <Tag color={stepStatus.color} style={{ margin: 0, fontSize: isBranchStep ? '11px' : '12px' }}>
                        {stepStatus.label}
                    </Tag>
                </div>

                {/* Step Properties */}
                <div style={{ display: 'flex', gap: '4px', flexWrap: 'wrap', marginTop: '8px' }}>
                    {step.isCritical && (
                        <Tooltip title="Critical Step">
                            <Tag color="red" style={{ fontSize: '10px', margin: 0 }}>
                                <WarningOutlined /> Critical
                            </Tag>
                        </Tooltip>
                    )}
                    {step.canRunInParallel && (
                        <Tooltip title="Can Run in Parallel">
                            <Tag color="green" style={{ fontSize: '10px', margin: 0 }}>
                                <ForkOutlined /> Parallel
                            </Tag>
                        </Tooltip>
                    )}
                    {step.maxRetries > 0 && (
                        <Tooltip title={`Max Retries: ${step.maxRetries}`}>
                            <Tag color="orange" style={{ fontSize: '10px', margin: 0 }}>
                                <RetweetOutlined /> {step.maxRetries}
                            </Tag>
                        </Tooltip>
                    )}
                </div>

                {/* Step Result Message */}
                {step.result && (
                    <div style={{ marginTop: '8px' }}>
                        <Text
                            type={step.result.isSuccess ? 'success' : 'danger'}
                            style={{ fontSize: '11px' }}
                            ellipsis
                        >
                            {step.result.message}
                        </Text>
                    </div>
                )}

                {renderBranches() }
            </Card>

            {/* Step Detail Modal */}
            <Modal
                title={
                    <Space>
                        <ControlOutlined />
                        Step Details: {step?.name}
                    </Space>
                }
                visible={stepDetailModal}
                onCancel={() => setStepDetailModal(false)}
                width={800}
                footer={[
                    <Button key="close" onClick={() => setStepDetailModal(false)}>
                        Close
                    </Button>
                ]}
            >
                {step && (
                    <Descriptions bordered column={2} size="small">
                        <Descriptions.Item label="Step Name" span={2}>
                            {step.name}
                        </Descriptions.Item>
                        <Descriptions.Item label="Status">
                            <Tag color={stepStatusConfig[step.status as StepStatusKey]?.color}>
                                {stepStatusConfig[step.status as StepStatusKey]?.label || step.status}
                            </Tag>
                        </Descriptions.Item>
                        <Descriptions.Item label="Critical">
                            {step.isCritical ?
                                <Tag color="red">Yes</Tag> :
                                <Tag color="green">No</Tag>
                            }
                        </Descriptions.Item>
                        <Descriptions.Item label="Can Run Parallel">
                            {step.canRunInParallel ? 'Yes' : 'No'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Idempotent">
                            {step.isIdempotent ? 'Yes' : 'No'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Max Retries">
                            {step.maxRetries || 0}
                        </Descriptions.Item>
                        <Descriptions.Item label="Retry Delay">
                            {step.retryDelay || '-'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Timeout">
                            {step.timeout || '-'}
                        </Descriptions.Item>

                        {step.stepDependencies?.length > 0 && (
                            <Descriptions.Item label="Step Dependencies" span={2}>
                                <Space wrap>
                                    {step.stepDependencies.map(dep => (
                                        <Tag key={dep} color="blue">{dep}</Tag>
                                    ))}
                                </Space>
                            </Descriptions.Item>
                        )}

                        {step.dataDependencies && Object.keys(step.dataDependencies).length > 0 && (
                            <Descriptions.Item label="Data Dependencies" span={2}>
                                <Space direction="vertical" style={{ width: '100%' }}>
                                    {Object.entries(step.dataDependencies).map(([key, value]) => (
                                        <div key={key}>
                                            <Text code>{key}</Text>: <Text type="secondary">{value}</Text>
                                        </div>
                                    ))}
                                </Space>
                            </Descriptions.Item>
                        )}

                        {step.result && (
                            <>
                                <Descriptions.Item label="Result Status" span={2}>
                                    {step.result.isSuccess ?
                                        <Tag color="success">Success</Tag> :
                                        <Tag color="error">Failed</Tag>
                                    }
                                </Descriptions.Item>
                                <Descriptions.Item label="Result Message" span={2}>
                                    <Text>{step.result.message}</Text>
                                </Descriptions.Item>
                                {step.result.data && Object.keys(step.result.data).length > 0 && (
                                    <Descriptions.Item label="Result Data" span={2}>
                                        <pre style={{
                                            background: '#f5f5f5',
                                            padding: '10px',
                                            borderRadius: '4px',
                                            maxHeight: '200px',
                                            overflow: 'auto'
                                        }}>
                                            {JSON.stringify(step.result.data, null, 2)}
                                        </pre>
                                    </Descriptions.Item>
                                )}
                            </>
                        )}

                        {(step.branches?.length ?? 0) > 0 && (
                            <Descriptions.Item label="Branches" span={2}>
                                <Space direction="vertical" style={{ width: '100%' }}>
                                    {(step.branches ?? []).map((branch, index) => (
                                        <Card key={index} size="small">
                                            <Text>Steps: {branch.steps?.join(', ') || 'None'}</Text>
                                            <br />
                                            <Text type="secondary">
                                                {branch.isDefault ? 'Default Branch' : `Condition: ${branch.condition}`}
                                            </Text>
                                        </Card>
                                    ))}
                                </Space>
                            </Descriptions.Item>
                        )}
                    </Descriptions>
                )}
            </Modal>
        </>
    );
};

export default FlowStep;