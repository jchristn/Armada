namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Services;

    /// <summary>
    /// Request body for POST /api/v1/tenants/lookup: the email to look up and which page of matching tenants to
    /// return (querystring pageNumber and pageSize override the body).
    /// </summary>
    public class TenantLookupRequest
    {
        #region Public-Members

        /// <summary>
        /// Email address to look up.
        /// </summary>
        public string Email
        {
            get => _Email;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Email));
                _Email = value;
            }
        }

        /// <summary>
        /// One-based page number. Default 1; values below 1 are normalized to 1.
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Tenants per page. Default 10, minimum 1, maximum 1000; values outside the range are normalized to the
        /// nearest bound.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = value < CollectionLimits.MinPageSize
                ? CollectionLimits.MinPageSize
                : (value > CollectionLimits.MaxPageSize ? CollectionLimits.MaxPageSize : value);
        }

        #endregion

        #region Private-Members

        private string _Email = "";
        private int _PageNumber = 1;
        private int _PageSize = CollectionLimits.DefaultPageSize;

        #endregion
    }
}
