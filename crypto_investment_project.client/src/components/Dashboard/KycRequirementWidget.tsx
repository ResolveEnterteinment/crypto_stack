import React, { useEffect, useState } from 'react';
import { Card, Alert, Button } from 'antd';
import { useNavigate } from 'react-router-dom';
import kycService from '../../services/kycService';

interface KycRequirementWidgetProps {
  onInitiateVerification?: () => void;
}

const KycRequirementWidget: React.FC<KycRequirementWidgetProps> = ({ onInitiateVerification }) => {
  const [isKycVerified, setIsKycVerified] = useState<boolean | null>(null);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();
  
  useEffect(() => {
    checkKycStatus();
  }, []);
  
  const checkKycStatus = async () => {
    try {
      setLoading(true);
      const response = await kycService.getKycStatus();
      if (response.success) {
        setIsKycVerified(response.data.status === 'Approved');
      }
    } catch (err) {
      console.error('Failed to check KYC status', err);
    } finally {
      setLoading(false);
    }
  };
  
  const startVerification = async () => {
    try {
      const response = await kycService.initiateVerification({
        userId: 'current',
        verificationLevel: 'STANDARD',
        userData: {}
      });
      
      if (response.success && response.sessionId) {
        navigate(`/kyc-verification?sessionId=${response.sessionId}`);
        
        if (onInitiateVerification) {
          onInitiateVerification();
        }
      }
    } catch (err) {
      console.error('Failed to initiate verification', err);
    }
  };
  
  if (loading || isKycVerified === true) {
    return null;
  }
  
  return (
    <Card>
      <Alert
        message="Verification Required"
        description="To access all platform features, please complete the identity verification process."
        type="warning"
        showIcon
        action={
          <Button size="small" type="primary" onClick={startVerification}>
            Verify Now
          </Button>
        }
      />
    </Card>
  );
};

export default KycRequirementWidget;