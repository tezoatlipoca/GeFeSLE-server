namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Helper methods for mapping between DTOs and internal domain objects.
    /// </summary>
    public static class DtoMappingExtensions
    {
        // User mappings
        public static UserResponseDto ToResponseDto(this GeFeSLEUser user, IEnumerable<string>? roles = null)
        {
            return new UserResponseDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                LastAccessTime = user.LastAccessTime,
                Roles = roles?.ToList() ?? new List<string>()
            };
        }

        public static UserSummaryDto ToSummaryDto(this GeFeSLEUser user)
        {
            return new UserSummaryDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email
            };
        }

        public static void UpdateFromDto(this GeFeSLEUser user, UserCreateUpdateDto dto)
        {
            user.UserName = dto.UserName;
            user.Email = dto.Email;
            user.PhoneNumber = dto.PhoneNumber;
        }

        // List mappings
        public static GeListResponseDto ToResponseDto(this GeList list, IEnumerable<GeFeSLEUser>? owners = null, IEnumerable<GeFeSLEUser>? contributors = null)
        {
            return new GeListResponseDto
            {
                Id = list.Id,
                Name = list.Name,
                Comment = list.Comment,
                CreatedDate = list.CreatedDate,
                ModifiedDate = list.ModifiedDate,
                CreatorId = list.CreatorId,
                CreatorName = list.Creator?.UserName,
                ActivityPubId = list.ActivityPubId,
                Visibility = list.Visibility,
                IsOrdered = list.isOrdered,
                VisibleItemCount = list.VisibleItemCount,
                Owners = owners?.Select(u => u.ToSummaryDto()).ToList() ?? list.ListOwners.Select(u => u.ToSummaryDto()).ToList(),
                Contributors = contributors?.Select(u => u.ToSummaryDto()).ToList() ?? list.Contributors.Select(u => u.ToSummaryDto()).ToList()
            };
        }

        public static void UpdateFromDto(this GeList list, GeListDto dto)
        {
            list.Name = dto.Name;
            list.Comment = dto.Comment;
            list.ActivityPubId = dto.ActivityPubId;
            list.Visibility = dto.Visibility;
        }

        // List item mappings
        public static GeListItemResponseDto ToResponseDto(this GeListItem item)
        {
            return new GeListItemResponseDto
            {
                Id = item.Id,
                ListId = item.ListId,
                Name = item.Name,
                Comment = item.Comment,
                IsComplete = item.IsComplete,
                Visible = item.Visible,
                IsDeleted = item.IsDeleted,
                RedirectToItemId = item.RedirectToItemId,
                Tags = new List<string>(item.Tags),
                CreatedDate = item.CreatedDate,
                ModifiedDate = item.ModifiedDate
            };
        }

        public static void UpdateFromDto(this GeListItem item, GeListItemCreateUpdateDto dto)
        {
            item.Name = dto.Name;
            item.Comment = dto.Comment;
            item.IsComplete = dto.IsComplete;
            item.Visible = dto.Visible;
            item.Tags = new List<string>(dto.Tags);
        }
    }
}
