using One.Domain.Entities.Identity;
using One.Domain.Enums;

namespace One.Shared.Services
{
    public class StateService
    {
        private UserData LoggedUser { get; set; }
        private bool LoggedInSuccessfully { get; set; } = false;

        private LanguageState Languange { get; set; } = LanguageState.English;
        public bool IsEnglishLanguage => Languange == LanguageState.English;

        public ListsData Lists { get; set; } = new();

        public async Task SetUser(UserData user)
        {
            LoggedInSuccessfully = true;
            LoggedUser = user;
            await Task.CompletedTask;
        }

        public async Task SetLanguage(LanguageState lang)
        {
            Languange = lang;
            await Task.CompletedTask;
        }

        public async Task ClearUser()
        {
            //User = null;
            LoggedInSuccessfully = false;
            Languange = LanguageState.English;
            await Task.CompletedTask;
        }
    }

    public class ListsData
    {
        //public List<FacultySelectOutput> FacultiesList { get; set; }
        //public List<UsersSelectOutput> UsersList { get; set; }
    }
}