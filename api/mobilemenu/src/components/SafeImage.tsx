import React, { useState } from 'react';

interface SafeImageProps extends React.ImgHTMLAttributes<HTMLImageElement> {
  fallbackType?: 'food' | 'drink' | 'general';
}

export const SafeImage: React.FC<SafeImageProps> = ({
  src,
  alt,
  className,
  ...props
}) => {
  const [hasError, setHasError] = useState(false);

  React.useEffect(() => {
    setHasError(false);
  }, [src]);

  const handleOnError = () => {
    setHasError(true);
  };

  if (hasError || !src) {
    return (
      <div className={`bg-[#FAF6F0] border border-[#EFECE6] ${className}`} aria-label={alt} />
    );
  }

  return (
    <img
      src={src}
      alt={alt}
      className={className}
      onError={handleOnError}
      {...props}
    />
  );
};
